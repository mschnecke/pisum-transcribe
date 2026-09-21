## Context

This change builds on `scaffold-app-shell` (host, settings, logging). Two libraries are current as of this plan, and both differ from what `docs/idea.md` describes:
- **SharpHook 8.0.0** (Aug 2026) moved hook configuration into `Run` / `RunAsync`, disabled `KeyTyped` by default, moved event simulation to `SharpHook.Simulation`, and moved the test hook to the `SharpHook.Testing` package.
- **NAudio 3.1.0** marks `WasapiCapture` obsolete in favor of `WasapiRecorder` / `WasapiRecorderBuilder`. The builder supports zero-copy `DataAvailable(ReadOnlySpan<byte>)`, shared-mode format conversion through `WithFormat`, and `WithDefaultDeviceStreamRouting()` + `BuildAsync()`, which makes capture follow the Windows default device.

This change delivers services only. `add-dictation-workflow` wires them to the UI.

## Goals / Non-Goals

**Goals:**
- Hotkey semantics as a pure, fully unit-tested state machine, separate from the OS hook.
- Low start latency, so the first word is not lost. Target: under 200 ms from the key press to the first real captured audio (not flagged silent and not all zeros), measured on the target laptop.
- Hermetic unit tests. Real microphone tests are explicit.

**Non-Goals:**
- No microphone device picker. Following the default device covers the laptop and headset case. A picker can be added later.
- No suppressing hotkey keys. That would require running handlers on the hook thread (`SimpleGlobalHook`) and would risk input lag system-wide.
- No mouse-button hotkeys, toggle mode, or hotkey editor. The editor comes in `add-settings-window`.
- No level meter or voice activity detection (see `add-voice-activity-detection`).

## Decisions

### D1: `PushToTalkDetector`, a pure state machine

`PushToTalkDetector(IReadOnlySet<KeyCode> hotkey)` has `OnKeyDown(KeyCode)`, `OnKeyUp(KeyCode)` and `Reset()`, and returns `PushToTalkSignal?` (`Pressed` | `Released` | `Cancelled`). It tracks the set of currently down hotkey keys, exposed read-only as `DownKeys` for the missed-release check (D3), and an `_active` flag:
- **Key down:** a key already in the down set counts as auto-repeat and is ignored. A hotkey key that completes the set while not active → `Pressed`. A non-hotkey key while active → `Cancelled`, then the state is set to "suppressed until all hotkey keys are up".
- **Key up:** a hotkey key while active → `Released`, then inactive. Once all hotkey keys are up, the suppressed flag clears.
- **Reset:** while active → `Cancelled`. Clears all state, including the down set and the suppressed flag.

*Why pure:* every scenario in the `push-to-talk-hotkey` spec becomes a plain unit test with no hook, no threads and no timing. The detector is not thread-safe; the adapter serializes all calls (D2).

### D2: SharpHook adapter

`SharpHookPushToTalkHotkey : IPushToTalkHotkey, IHostedService` owns an `EventLoopGlobalHook` created with a background event-loop thread, and starts it with `RunAsync(GlobalHookType.Keyboard, useBackgroundThread: true)`. A keyboard-only hook installs no low-level mouse hook, so mouse input never passes through the process. Handlers run on SharpHook's event-loop thread, so a slow consumer never delays system input. `KeyPressed` / `KeyReleased` feed the detector, and the resulting signals raise the C# events `Pressed` / `Released` / `Cancelled`. Consumers marshal to their own context.

- **Simulated events:** events with `IsEventSimulated` are dropped before they reach the detector. SharpHook's libuiohook sets the flag for any input with `LLKHF_INJECTED` or `LLKHF_LOWER_IL_INJECTED`, so this covers the paste keystrokes of `add-text-insertion` and every other application's `SendInput`.
- **Raw key codes:** on key down, the adapter records `KeyboardEventData.RawCode` for each hotkey key. On Windows, libuiohook stores the left/right-distinguishing virtual-key code there, which the missed-release check (D3) passes to `GetAsyncKeyState` without a mapping table.
- **Serialization:** hook handlers and the missed-release timer run on different threads. One `lock` guards the detector, the raw key codes and the timer state. Signals are raised inside the lock, so consumers always see them in order. Handlers must return quickly; `DictationController` only writes to a channel.
- **Key privacy:** the hook sees every key on the system, including passwords. Signals are logged at Debug level without key names or codes, and non-hotkey keys never reach a log call. No SharpHook `LogSource` is attached, so libuiohook writes no logs of its own. `KeyTyped` stays disabled (the SharpHook 8 default), so key codes are never translated into typed characters. Event data is not kept beyond the handler; the detector keeps only hotkey keys, and the adapter keeps only their raw codes. Only the configured hotkey may appear in logs, for example in the invalid-setting warning.
- **Hook failure:** if `RunAsync` throws or its task faults with a `HookException`, for example because the hook could not be installed, the adapter logs an error and shows a tray notification that push-to-talk is unavailable, marshalled to the UI thread. A task that completes because the hook was disposed on stop is not a failure.
- **Stop:** `StopAsync` disposes the hook and the timer.

`IGlobalHook` is injected, so tests use `SharpHook.Testing.TestGlobalHook`, whose `EventMask` and `KeyCodeToRawCode` let tests mark events as simulated and supply raw codes, and whose `RunResult` makes the start fail.

`services.AddRecording()`, called from `AppHost.Create`, registers the adapter as `IPushToTalkHotkey` and as a hosted service, and registers the audio recorder (D5). It also registers `TimeProvider.System` with `TryAddSingleton`, because the missed-release check and the recorder's start timeout take a `TimeProvider`.

A `Suspend()` / `Resume()` pair is **not** added here. The hotkey editor in `add-settings-window` adds it when needed.

### D3: Missed-release check

A low-level hook in a non-elevated process receives no input while an elevated window has focus (UIPI), and none on the secure desktop (UAC prompt, lock screen). A key released there leaves the detector believing it is held. The recording would run to its maximum duration and then transcribe and insert audio the user did not mean to dictate, and the next real press would be ignored as auto-repeat.

While `DownKeys` is not empty, a timer from the injected `TimeProvider` ticks every 250 ms. On each tick, the adapter checks each down key with `GetAsyncKeyState`, injected as `Func<int, bool> isKeyDown` for tests, using the recorded raw key code. A key that reads as up on **two consecutive ticks** calls `Reset()`, which raises `Cancelled` if the hotkey was active. A reading of down restarts that key's count. Two readings keep a normal release, whose key-up event arrives within milliseconds, from turning into a cancel. The worst case until the reset is about 500 ms, within the 1 second the spec allows. The timer stops when `DownKeys` becomes empty, so the check costs nothing while idle.

`GetAsyncKeyState` returns 0 when UIPI blocks access to the foreground thread or when the current desktop is not the active desktop. Both read as "up", so holding the hotkey while an elevated window, the UAC prompt or the lock screen comes to the front cancels. That is intended: dictating into elevated windows is not supported and also fails at paste time (see `add-text-insertion`).

This check also meets the "Reset on session switch" requirement: locking the workstation or switching the user makes the desktop inactive, so held keys read as up and the state resets. The adapter does not subscribe to `SystemEvents.SessionSwitch`.

### D4: Hotkey setting representation

`RecordingSettings` is saved as the `recording` section and follows the shape rules in `AppSettings`. The list default is not a compile-time constant, so `Hotkey` is an `init` property with an initializer, not a constructor parameter:

```csharp
internal sealed record RecordingSettings
{
    public IReadOnlyList<string> Hotkey { get; init; } = ["VcRightControl"];
}
```

`AppSettings` gains `public RecordingSettings Recording { get; init; } = new();`, and `JsonSettingsStore.Load` replaces a `null` section with `new RecordingSettings()`, as it does for the existing sections.

The values are SharpHook `KeyCode` enum names, parsed with `Enum.TryParse(ignoreCase: true)`. A `null` or empty list, or any unknown name → the default plus a warning. The file stores names as strings, not `KeyCode` values, for two reasons:
- SharpHook 8 renumbered `KeyCode`, so numeric codes are not stable.
- A `KeyCode`-typed property would make one unknown name throw `JsonException` during deserialization, and `JsonSettingsStore` would rename the whole file to `.corrupt` and reset every setting.

Parsing ignores case, because the settings file writes enum values in camelCase while `KeyCode` names are PascalCase.

*Why right Ctrl:* a modifier-only hotkey does nothing harmful when it passes through to the focused app. Unlike Space or letter combinations, it can be held comfortably, and the ThinkPad E14 has a physical right Ctrl. Its common use in shortcuts (Ctrl+C) is exactly what the cancel semantics handle.

### D5: `AudioRecorder` and the capture session

```csharp
internal interface IAudioRecorder
{
    bool IsRecording { get; }
    event EventHandler? MaxDurationReached;
    event EventHandler<RecordingFailedException>? Failed;
    Task StartAsync(TimeSpan maxDuration, CancellationToken ct);
    Task<AudioClip> StopAsync();
    Task AbortAsync();
}
internal sealed record AudioClip(float[] Samples) { public TimeSpan Duration => ... / 16000; }
```

`RecordingFailedException` is the abstract base of `MicrophoneAccessDeniedException`, `NoMicrophoneException`, `MicrophoneMutedException`, `MicrophoneNotRespondingException` and `MicrophoneDisconnectedException`. Each message is fit for the user.

**Seam:** `AudioRecorder` owns the state and the samples, and reaches NAudio only through a capture session, the same pattern as `INativeSpeechEngine` in the transcription engine:

```csharp
internal delegate void SamplesAvailableHandler(ReadOnlySpan<float> samples, bool silent);

internal interface ICaptureSessionFactory
{
    Task<ICaptureSession> CreateAsync(CancellationToken cancellationToken);
}

internal interface ICaptureSession : IAsyncDisposable
{
    event SamplesAvailableHandler? SamplesAvailable; // capture thread
    event EventHandler<Exception?>? Stopped;          // null when stopped by request
    void Start();
    Task StopAsync();
}
```

`WasapiCaptureSession` and its factory are the only NAudio code. The contract below, including its races, is unit-tested with a fake session; the NAudio part is covered by the hardware tests.

**Contract:**

| Call | `Idle` | `Recording` | `LimitReached` | `Failed` |
|---|---|---|---|---|
| `StartAsync` | Opens the microphone, completes when audio flows → `Recording` | `InvalidOperationException` | `InvalidOperationException` | Clears the stored error, opens, completes when audio flows → `Recording` |
| `StopAsync` | `InvalidOperationException` | Stops, returns the audio → `Idle` | Returns the kept audio → `Idle` | Throws the stored error → `Idle` |
| `AbortAsync` | No effect | Stops, discards the audio → `Idle` | Discards → `Idle` | Clears the stored error → `Idle` |

- **Start completes when audio flows:** `StartAsync` opens the session and completes on the first real packet: one that WASAPI does not flag as silent and whose samples are not all zero. Leading packets that are flagged silent or hold only digital zeros are dropped, because they are digital silence while a device wakes up or a Bluetooth headset switches profiles. The flag alone is not enough: the Intel Smart Sound microphone array on the target laptop sends unflagged zero packets while its DSP wakes up and then pauses for up to about 370 ms, so completing on the flag made the start about 0.4 s early (see `notes.md`). `AudioRecorder` makes the zero check, so the fake session covers it. After the first real packet, flagged and zero packets are kept, because they are real pauses. If no real packet arrives within 3 s, measured with the injected `TimeProvider`, the session is disposed, the state returns to `Idle`, and `StartAsync` throws `MicrophoneNotRespondingException`. The `CancellationToken` of `StartAsync` cancels a pending start the same way, with `OperationCanceledException`. While a start is pending, `StopAsync` and `AbortAsync` throw `InvalidOperationException`, because the caller awaits the start.
- **Limit reached** (`Recording` → `LimitReached`): when the sample count reaches `maxDuration × 16000`, the accumulator truncates to the limit and accepts no more samples. The callback does not stop NAudio from inside its own capture callback. It sets the state and queues the work to the thread pool, which stops and disposes the session, then raises `MaxDurationReached` once. The microphone is released even if the caller never calls `StopAsync`.
- **Microphone lost** (`Recording` → `Failed`): `Stopped` with an exception discards the samples, disposes the session, stores a `MicrophoneDisconnectedException` and raises `Failed` once. No stop or abort is needed before the next `StartAsync`.
- **Start failure:** a typed exception from `CreateAsync` or `Start` disposes the session, leaves the state `Idle` and propagates. Cancellation during `CreateAsync` does the same with `OperationCanceledException`.
- **Stop racing an error:** once `StopAsync` has begun, a session error does not change the outcome. The audio captured so far is returned, and `Failed` is not raised.
- **Pending work:** `StopAsync` and `AbortAsync` in `LimitReached` or `Failed` await the queued session stop, so the microphone is released when they return.
- **Disposal:** `AudioRecorder` implements `IAsyncDisposable`. `DisposeAsync` aborts in any state, including a pending start, and disposes the session. `ShutdownCoordinator` disposes the host after stopping it, so the container disposes the recorder and the microphone is released at shutdown within the watchdog time. Calls after disposal throw `ObjectDisposedException`.
- **Threads:** `IsRecording` is true only in `Recording`. Events are raised on thread-pool threads, never on the UI thread. A `lock` guards the state and the accumulator; NAudio calls and awaits run outside it.

**Start:** `new WasapiRecorderBuilder().WithDefaultDeviceStreamRouting().WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(16000, 1)).WithBufferLength(20).BuildAsync()`, then `StartRecording()`. The recorder is built off the UI thread, so `RecordingStopped` is not marshalled to the WPF dispatcher. A new recorder per recording keeps the microphone closed while idle. No stream category is set: NAudio knows the `Speech` and `VoiceTyping` categories, but `WasapiRecorderBuilder` has no setter for them, so trying them is post-MVP tuning.

**Capture:** `WasapiCaptureSession` reinterprets the `DataAvailable` span with `MemoryMarshal.Cast<byte, float>` and passes it to `SamplesAvailable`, with `silent` set from `AudioClientBufferFlags.Silent`. A packet flagged silent is passed as zeros of the same length, because Windows asks to ignore its data. `AudioRecorder` appends it to a `SampleAccumulator` backed by an `ArrayBufferWriter<float>` pre-sized for 30 s. `SampleAccumulator.Append` clamps each sample to [-1, 1] as it copies it, because Windows' float conversion does not clamp: with a USB headset, a loud peak reached 1.32 (see `notes.md`).

**Session stop:** `StopAsync` calls `StopRecording()` and awaits `RecordingStopped`; `DisposeAsync` disposes the recorder. `AudioRecorder` then returns a copy of the accumulated samples on stop, or clears them on abort, as the contract says.

**Format conversion:** in standard shared mode, `WasapiRecorder` initializes the audio client with `AutoConvertPcm`, so Windows converts the device's mix format to 16 kHz mono float. No managed downmix or resampler is added. If the hardware test shows the format is rejected on the target laptop, a managed conversion is designed then.

**Mute check:** before building the recorder, the factory gets the default capture endpoint with `MMDeviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console)` and reads `AudioEndpointVolume.Mute`. A muted endpoint → `MicrophoneMutedException`, and the microphone is never opened. The check costs a few milliseconds and runs only at start; muting during a recording is not detected. A missing default endpoint surfaces here as `E_NOTFOUND`.

**Error mapping** (a pure function in `WasapiCaptureSession`, unit-tested):

| Condition | Result |
|---|---|
| `E_ACCESSDENIED` / `UnauthorizedAccessException` on build or start | `MicrophoneAccessDeniedException`; the message points to Settings › Privacy & security › Microphone and says access must be allowed for desktop apps and for Pisum Transcribe |
| `E_NOTFOUND` / no default capture endpoint, at the mute check, build or start | `NoMicrophoneException` |
| Default capture endpoint muted at start | `MicrophoneMutedException`; the message says to unmute the microphone, for example with the microphone key |
| `RecordingStopped` with an exception while recording | `Stopped` with that exception → `MicrophoneDisconnectedException` in `AudioRecorder` |

**Resolved: how Windows reports blocked access.** Windows has three switches under Settings › Privacy & security › Microphone: *Microphone access* for the device (`HKLM\...\CapabilityAccessManager\ConsentStore\microphone`), *Let apps access your microphone* (the same path under `HKCU`) and *Let desktop apps access your microphone* (`HKCU\...\ConsentStore\microphone\NonPackaged`). Microsoft documents `E_ACCESSDENIED` at activation only for packaged apps, so the first audio task tested this unpackaged app on the target laptop, one switch at a time. With any switch off, the recorder builds, and `StartRecording()` then fails with `E_ACCESSDENIED` (`0x80070005`). No switch delivers silence (see `notes.md`). The error mapping above is therefore enough, and the factory does not read the `ConsentStore` values.

**Resolved: the only microphone unplugged.** NAudio documents that routing moves capture to the new default device when the current one is unplugged, but not what happens when no device remains. Manual check (d) showed that the stream does not stall. About 0.4 s after the last audio, `RecordingStopped` reports `E_NOTFOUND` (`0x80070490`), and `AudioRecorder` raises `Failed` with `MicrophoneDisconnectedException` (see `notes.md`). No watchdog for missing samples is needed.

### D6: Start latency

Building and activating a recorder per press costs device activation time: routed activation, audio client initialization, possibly a DSP wake-up on array microphones, and one 20 ms buffer. A Bluetooth headset adds its switch to the headset profile, often 0.5 to 2 s, during which packets can arrive flagged as silent.

**Measurement:** `StartAsync` now ends at the first real audio, so the explicit hardware test measures its duration. In the app, `add-dictation-workflow` logs the time from the key press to the completed start. For finer analysis, NAudio passes each packet's QPC position, which is on the same clock as `Stopwatch.GetTimestamp()`.

**Mitigations:** the earlier ideas do not help much. Preparing the recorder on the first key of a chord does nothing for the single-key default, right Ctrl. Keeping an initialized but not started client might make Windows report the microphone as in use, which would conflict with "microphone opened only while recording". This design therefore makes the cue honest instead of making the start faster: `add-dictation-workflow` shows a starting look at the press and "Recording" only once `StartAsync` completes.

**Measured on the target laptop:** with the built-in microphone array, real audio arrives about 0.6 to 1.0 s after the start, and `StartRecording()` alone blocks for 190 to 610 ms (see `notes.md`). This misses the 200 ms target. Decision: accepted for now. The two-stage cue tells the user when to speak, and the mitigations are revisited when `add-dictation-workflow` is built, with its key-press-to-start measurements. The spec does not change.

## Risks / Trade-offs

- [A low-level keyboard hook in a non-elevated process does not receive keys while an elevated window, the UAC prompt or the lock screen has focus (UIPI, secure desktop).] → The missed-release check (D3) cancels a held hotkey within about 500 ms. Dictating into elevated apps is not supported; it also fails at paste time (see `add-text-insertion`). Running elevated is a non-goal.
- [Keys remapped by tools such as PowerToys Keyboard Manager or AutoHotkey arrive as simulated events and are ignored, so a remapped key cannot act as the hotkey.] → Accepted. The physical key still works.
- [The hook is keyboard-only, so clicking or scrolling with the mouse while holding the hotkey, for example right Ctrl+scroll to zoom, does not cancel. The dictation runs and transcribes whatever the microphone picked up.] → Accepted. It is unlikely for right-handed mouse users, and the overlay appears at the press, so the dictation is visible. Polling the mouse buttons in the missed-release timer (D3) could be added later; it would catch clicks but not scrolling.
- [Windows removes low-level hooks that time out (`LowLevelHooksTimeout`).] → SharpHook's `EventLoopGlobalHook` returns from the hook callback immediately and processes events on its own thread. The hook is keyboard-only, and the detector is O(1).
- [Hotkey signals are raised inside the adapter's lock.] → Consumers must return quickly. `DictationController` only writes to a channel.
- [On the company-managed target laptop, the endpoint protection may classify a system-wide keyboard hook in an unsigned build as keylogger behavior. Application control is not enforced today, but a policy rollout could block unsigned builds or the `%LOCALAPPDATA%` install location later.] → Task 2.2 checks the endpoint protection's reaction on the target laptop early, before the audio work. IT is informed up front, with "Key events stay private" as the description of what the hook sees and keeps. Code signing is decided together with packaging, informed by IT's answer.
- [An unlisted `NAudio.Wasapi` 22.0.0 on NuGet could be picked up by floating version ranges.] → Exact pin to 3.1.0 in central package management.
- [Experimental Windows 11 builds (26340, August 2026) show a consent dialog when a desktop app first uses the microphone, and replace the shared desktop-apps switch with one switch per app. Microsoft documents that microphone activation must run on the UI thread for such a dialog to appear, and this design opens the microphone off the UI thread.] → Not designed for yet, because Microsoft has not committed the feature to a release. The access-denied message already points to the Microphone page, which covers per-app switches. Revisit when it ships, for example with a first microphone use from the UI thread at the end of model setup.
- [Bluetooth headsets delay the start by their profile switch, and a slow device might exceed the 3 s start timeout.] → The two-stage cue in `add-dictation-workflow` tells the user when to speak. The timeout is one constant and can be raised with real measurements.
- [Automatic stream routing requires Windows 10 1607 or later.] → Accepted. The project sets no `SupportedOSPlatformVersion`, and the routing API carries no platform annotation, so the build is unaffected. Windows versions before 1607 are not targeted.
- [Many short-lived recorders might leak COM objects.] → `DisposeAsync` runs on every path. The explicit hardware test runs 100 warm-up start/stop cycles, then 50 more, and checks that the process handle count grows by at most 20 over those 50. The warm-up is needed because the Windows audio stack allocates a one-time step of about 130 handles in the first cycles, which then stays flat over 200 cycles (see `notes.md`).
