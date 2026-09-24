## Context

See proposal.md for the motivation. The decisions come from the section "Decided for later macOS changes" of the archived `add-macos-shell` design (2026-09-22) and from explore mode on 2026-09-24, which changed three of them (D4, D8 and D11 below). D-numbers of earlier designs are written "shell D8" and "setup D4".

**Current state:**
- `AddRecording()` registers `SharpHookPushToTalkHotkey` (a hosted service on SharpHook's `EventLoopGlobalHook`), `WasapiCaptureSessionFactory` and `AudioRecorder` on Windows only. On macOS it registers `InactivePushToTalkHotkey`, which the settings window's hotkey editor uses, and no recorder.
- `AddDictation()` isn't registered on macOS, so nothing there consumes the hotkey's signals or starts a recording (`add-macos-dictation`, #20).
- `SharpHookPushToTalkHotkey` is nearly platform-neutral. The exception is its missed-release timer: it reads a held key through `GetAsyncKeyState`, a `DllImport` in the shared file, with the Windows virtual-key code from `e.Data.RawCode`. Tests replace it through the `isKeyDown` constructor parameter.
- Windows has no code for "Reset on session switch". The missed-release timer covers it, because `GetAsyncKeyState` reads *up* while another desktop is active.
- `AudioRecorder` is platform-neutral. `ICaptureSessionFactory` and `ICaptureSession` are the platform seam: 16 kHz mono float, a `silent` flag per packet, and a `Stopped` event with the error. The exceptions `MicrophoneAccessDeniedException`, `MicrophoneMutedException`, `NoMicrophoneException` and `MicrophoneDisconnectedException` are shared.
- The default hotkey is `HotkeyParser.DefaultKeyName` (`VcRightControl`), which is also `RecordingSettings.Hotkey`'s initializer. `HotkeyText` and `HotkeyRecorder` hold the key names and the valid-hotkey rule with Windows names.
- `add-macos-setup` gives `IPermissions.IsAccessibilityGrantedAtStart`, the helper's `pisum_microphone_status`, and `RelaunchService`, whose check timer runs only when the grant was missing at start.

**Facts that shape this design:**
- **Spike M3:** SharpHook's hook runs next to Avalonia's main loop. Without the grant it fails at once with `ErrorAxApiDisabled`, and it sees a grant only in a new process. The app's own posted Cmd+V arrives with `IsEventSimulated=True`.
- **SharpHook 8.0.0** (its XML docs): `UioHookProvider.PromptUserIfAxApiDisabled` is `true` by default, so the hook would show macOS's Accessibility prompt on its own. `AxPollFrequency` (1 s) polls the grant while the hook runs, which ends it with `ErrorAxApiRevoked`. `KeyTypedEnabled` is `false` by default.
- **libuiohook's macOS build** (its strings) dispatches "system key" events (media, volume and brightness keys) with `dispatch_sync_f` to the main queue, from inside the tap callback, and logs `CGEventTap timeout!` when macOS disables the tap.
- **CoreAudio** has no silence flag like WASAPI's. A denied microphone and a muted device deliver digital zeros and no error (shell, "Decided for later macOS changes"). When the last input device goes away, an AudioQueue input simply stops calling back.

## Goals / Non-Goals

**Goals:**
- On macOS, `IPushToTalkHotkey` and `IAudioRecorder` behave as the specs say, so that `add-macos-dictation` only has to register `AddDictation()` there.
- Windows behaves exactly as today. Its code changes only where a seam moves into `Windows/`.

**Non-Goals:**
- A consumer of the hotkey on macOS, the tray states, the overlay and the error notifications (#20). With them go the macOS wording of the `dictation` requirements "Error notifications" and "Dictation ends when the application exits", and the check by hand of a denied microphone in the app.
- A dev-only way to see the hotkey work, such as a test menu item. The Debug log is enough (D10).
- The secure-input hint in the tray (`add-macos-text-insertion`).

## Decisions

### D1: The shared hotkey service on macOS, with two platform seams

`SharpHookPushToTalkHotkey` stays one class for both platforms. What differs moves behind two seams that `AddRecording()` picks with `#if WINDOWS`:

- **`IHotkeyKeyState`**, "is this key still observably held?", by the hook's raw code. It replaces the `isKeyDown` delegate and the `DllImport`:
  - `Windows/WindowsHotkeyKeyState`: `GetAsyncKeyState` through `Windows.Win32.PInvoke` (CsWin32; the function is in `NativeMethods.txt` already).
  - `MacOS/MacHotkeyKeyState`: see D2.
- **`IHookAccess`**, whether the hook may start and what to do when the grant is revoked:
  - `Windows/`: always may start, and a revoke can't happen.
  - `MacOS/`: reads and reports through `IPermissions` (D3).

`InactivePushToTalkHotkey` and its tests are removed.

*Rejected:* a separate `MacPushToTalkHotkey`. The detector, the suspension, the missed-release timer and the privacy rules would be copied, and the two would drift.

### D2: Lock and user switch folded into the missed-release timer

On macOS, the physical key state comes from `CGEventSourceKeyState(kCGEventSourceStateHIDSystemState, keycode)`. SharpHook's raw code on macOS is the CG key code. That state still reads *down* behind the lock screen while the user keeps holding the key. The shell design planned two notifications for that: the distributed `com.apple.screenIsLocked` and `NSWorkspace`'s session notifications through the helper.

Instead, `MacHotkeyKeyState` answers "observably held" as:

```
held = CGEventSourceKeyState(HIDSystemState, keycode)
       && session on console      (kCGSessionOnConsoleKey)
       && screen not locked       (CGSSessionScreenIsLocked)
```

It reads the last two from `CGSessionCopyCurrentDictionary()`, a CoreGraphics C function, through `DllImport`. The timer runs only while a hotkey key is held, as today, and cancels within 1 second. That's the same shape by which Windows already meets "Reset on session switch".

- One mechanism in place of three, no callback from AppKit into managed code, and no polling while idle.
- The helper needs no new function, so its ABI stays 2 (D11).
- `CGSSessionScreenIsLocked` isn't documented by Apple. `com.apple.screenIsLocked` isn't documented either. A hardware check covers it (D10).

*Rejected:* the notifications from the shell design, which would reset at once rather than within 1 second, at the cost of a new seam, two mechanisms and the ABI bump.

### D3: The Accessibility grant: never prompt, skip without it, and report a revoke

- **No prompt from the hook.** `AddRecording()` sets `UioHookProvider.Instance.PromptUserIfAxApiDisabled = false` on macOS before the hook is created. Only the setup window asks for the grant (`macos-permissions`).
- **Without the grant at start** (`IPermissions.IsAccessibilityInEffect` is false), `StartAsync` doesn't run the hook. It logs at Information that push-to-talk waits for the Accessibility grant, and it shows no notification. The setup window and **Set up Pisum Transcribe…** already say that the grant is missing, and during a first run the user is granting at that moment.
- **A revoke while running** ends `RunAsync` with `ErrorAxApiRevoked`. `RunHookAsync` then:
  - shows its own notification: title "Push-to-talk stopped", message "Accessibility access for Pisum Transcribe was turned off. Choose Set up Pisum Transcribe… in the menu to allow it again."
  - reports the revoke through `IHookAccess`, which calls `IPermissions.OnAccessibilityRevoked()` on the UI thread (`IUiDispatcher`)
- **Any other failure** keeps today's "Push-to-talk unavailable" notification, on both platforms.

*Rejected:*
- **Starting the hook anyway and notifying on `ErrorAxApiDisabled`.** That would show a third message for the missing grant at every launch, in the middle of the first run.
- **One notification text for the revoke and other failures.** "Details are in the log" doesn't tell the user what to do after a revoke.

### D4: The grant "in effect" in place of "granted at start"

With setup's code, a grant after a revoke doesn't relaunch: `RelaunchService` polls only when the grant was missing at start, and `PermissionsViewModel.AreRequiredGranted` treats the grant from the start as in effect. So the window would close as complete while the hook stays dead. The spec already requires the relaunch on any change from not granted to granted. The delta in `macos-permissions` adds the revoke scenario.

- `IPermissions.IsAccessibilityGrantedAtStart` becomes `IsAccessibilityInEffect`: true at start when the process is trusted, and false from `OnAccessibilityRevoked()` on. A change raises `AccessibilityInEffectChanged`.
- `RelaunchService` starts its check timer at startup when the grant isn't in effect, as today, and also when `AccessibilityInEffectChanged` turns it false. The relaunch rules of setup D4 then apply unchanged, including waiting for a download.
- `PermissionsViewModel.AreRequiredGranted` reads `IsAccessibilityInEffect`. `ReadRequiredGranted()`, for the menu, stays as it is.

**The hook reports the revoke** (explore mode, option 1). It's the one part that knows that the grant stopped being in effect *for this process*, and SharpHook already polls the grant while the hook runs, so nothing new polls while the app sits idle.

*Rejected:* running `RelaunchService`'s timer for the whole run and watching `AXIsProcessTrusted()` fall. That adds a second 1 s poll next to SharpHook's own, and M3 showed that `AXIsProcessTrusted()` doesn't match what the hook sees.

### D5: The default hotkey per platform, without a migration

`HotkeyParser.DefaultKeyName` and `DefaultHotkey` become `VcRightMeta` on macOS and stay `VcRightControl` on Windows (`#if WINDOWS`). `RecordingSettings.Hotkey` already takes its initializer from there. Tests that hard-code `"VcRightControl"` for the default (`JsonSettingsStoreTests`, `HotkeyParserTests`) assert `HotkeyParser.DefaultKeyName` instead, so they hold on both hosts.

A Mac `settings.json` from a dev build of #15 or #16 can already store `["VcRightControl"]`. It keeps it. No Mac build was released, so there's no migration.

### D6: Key names and the valid-hotkey rule per platform

The tables move out of `HotkeyText` and `HotkeyRecorder` into a `HotkeyKeyNames` record with two instances, `Windows` and `MacOS`, and `Current`, picked with `#if WINDOWS`. Each holds the modifier order, the names, the keys that make a hotkey valid, and the rejection message. `HotkeyText` and `HotkeyRecorder` take the table, defaulting to `Current`. Both tables are plain data, so the tests of both run on either host.

| | Windows | macOS |
|---|---|---|
| Order | Ctrl, Alt, Shift, Win | fn, Control, Option, Shift, Command |
| Names | Left/Right Ctrl, Alt, Shift, Win | fn, Left/Right Control, Option, Shift, Command |
| Valid with | the modifiers above or F1–F24 | the modifiers above, fn or F1–F24 |
| Message | "The hotkey must include Ctrl, Alt, Shift, the Windows key or a function key F1–F24." | "The hotkey must include Control, Option, Shift, Command, fn or a function key F1–F24." |

The order follows Apple's order for shortcuts (fn first, then ⌃ ⌥ ⇧ ⌘), spelled out in words, as Windows spells its keys.

**The fn hint:** `DictationSectionViewModel.ShowsFnHint` is true on macOS while the hotkey shown, saved or just captured, contains `VcFunction`. The dialog shows it as a `hint` line under the hotkey: "Set "Press 🌐 key to" to "Do Nothing" in System Settings → Keyboard, or every press also runs that action."

*Rejected:*
- **Always showing the hint on macOS.** It's noise for the default, right Command.
- **Reading macOS's setting** (`AppleFnUsageType` in `com.apple.HIToolbox`) to show the hint only when needed. The key isn't documented, and the value would go stale while the window is open.
- **A link that opens Keyboard settings.** One more `x-apple.systempreferences:` anchor that Apple renames between releases.
- **Apple's key symbols** in place of words.

### D7: Capture on AudioQueue through `DllImport`

`MacOS/AudioQueueCaptureSessionFactory` and `MacOS/AudioQueueCaptureSession` call AudioToolbox and CoreAudio, both C APIs, through `DllImport`, as the project's rule says. The Swift helper isn't involved.

`CreateAsync` checks, in this order, before any microphone is opened:
1. **The permission:** `pisum_microphone_status` through `IUiDispatcher.InvokeAsync`, because helper calls run on the UI thread. Anything other than 3 (allowed) throws `MicrophoneAccessDeniedException`. That includes 0 (not asked yet), because a press of the hotkey never shows macOS's prompt.
2. **The device:** `kAudioHardwarePropertyDefaultInputDevice`. `kAudioObjectUnknown` throws `NoMicrophoneException`.
3. **Mute:** `kAudioDevicePropertyMute` in the input scope, if the device has that property (`AudioObjectHasProperty`). A value of 1 throws `MicrophoneMutedException`.

Then it opens the queue:
- `AudioQueueNewInput` with linear PCM, Float32, 16,000 Hz, mono, packed. AudioQueue converts from the device's format. `inCallbackRunLoop` is null, so the callback runs on AudioQueue's own thread.
- The callback is a `static` `[UnmanagedCallersOnly]` method. Its user data is a `GCHandle` to the session, freed after the queue is disposed. It raises `SamplesAvailable(samples, silent: false)` and enqueues the buffer again unless the session is stopping.
- The queue is pinned to the device that was checked, through `kAudioQueueProperty_CurrentDevice` with the device's UID.
- There are three buffers of 50 ms each.
- `StopAsync` calls `AudioQueueStop(immediate: true)` and then `AudioQueueDispose`, and raises `Stopped(null)`.

`AudioRecorder` doesn't change. Its "all zeros is silence" rule already covers macOS, where `silent` is always false.

*Rejected:*
- **Capture in the Swift helper** (AudioQueue or `AVAudioEngine`). AudioQueue is C, so the helper would break its own rule ("Objective-C only"), add a second callback hop and grow the ABI.
- **`AVAudioEngine`**, which is Objective-C and changes the input's voice processing and routing in ways the app doesn't need.

### D8: The session owns the device: follow the default, detect the loss

An AudioQueue input doesn't reliably follow a change of the default input device. When the last input device goes away, it just stops calling back, so the recording would hang until its maximum duration. So the session listens for changes of `kAudioHardwarePropertyDefaultInputDevice` (`AudioObjectAddPropertyListener`, another `[UnmanagedCallersOnly]` callback, on CoreAudio's notification thread):

- **A new default device:** the session disposes its queue and opens a new one, pinned to the new device, within the same `ICaptureSession`. `AudioRecorder` sees a gap of tens of milliseconds and nothing else. The swap runs off CoreAudio's notification thread (`Task.Run`), under the session's lock, and never after `StopAsync`.
- **`kAudioObjectUnknown`:** the session raises `Stopped(new MicrophoneDisconnectedException())`, as WASAPI's session does.
- **The listener** is removed in `StopAsync` and `DisposeAsync`, before the `GCHandle` is freed.

The new device's mute state isn't checked on a swap. That matches Windows, where the mute check runs only at start.

*Rejected:* trusting AudioQueue's own behavior and checking it with hardware. The behavior would then rest on something Apple doesn't document for input, and the loss of the last device would still need the listener.

### D9: The hook's threads on macOS

- The hook keeps `EventLoopGlobalHook(useBackgroundThreadForEventLoop: true)`, as on Windows. Its handlers only feed the detector under the lock and raise signals, so the tap callback stays fast.
- `KeyTypedEnabled` stays `false`. When on, every key would go to the main queue from inside the tap callback.
- libuiohook still sends system keys (media, volume, brightness) to the main queue. A blocked UI thread would therefore stall the tap until macOS disables it. The project's rule against blocking the UI thread already covers this. A hardware check confirms that the tap works again after `kCGEventTapDisabledByTimeout` (D10).
- `StopAsync` disposes the hook, which stops its `CFRunLoop`. At shutdown, `DispatcherWait` keeps the main run loop running, so a pending dispatch to the main queue drains, and the stop fits `HostOptions.ShutdownTimeout`.

### D10: Tests and checks by hand

- **Unit, on both hosts:**
  - `SharpHookPushToTalkHotkeyTests` with a fake `IHotkeyKeyState` in place of `isKeyDown`: a key that stops being observable (lock, off console) cancels within 1 second.
  - Also with a fake `IHookAccess`: without the grant the hook isn't run, and nothing is notified; `ErrorAxApiRevoked` shows the revoke text and reports it; other results show "Push-to-talk unavailable".
  - `HotkeyParserTests` and `JsonSettingsStoreTests` against `DefaultKeyName`.
  - `HotkeyTextTests` and `HotkeyRecorderTests` for both `HotkeyKeyNames` tables. `DictationSectionViewModel` shows the fn hint only on macOS with `VcFunction`.
  - `RelaunchService` and `PermissionsViewModel` with a fake `IPermissions` whose grant drops.
- **macOS `Integration`** (the real APIs, no hardware):
  - `CGEventSourceKeyState` reads an idle key as up
  - `CGSessionCopyCurrentDictionary` reports the session on console and the screen not locked
  - the default input device and its mute property can be read
  - `AudioQueueCaptureSessionFactory` fails with `MicrophoneAccessDeniedException` for a fake status that isn't 3
- **macOS `Hardware`** (`[Fact(Explicit = true)]`):
  - `Recording/MacOS/AudioRecorderHardwareTests`, mirroring the Windows ones on `AudioQueueCaptureSessionFactory`: 2 s gives about 32,000 samples (which proves the conversion to 16 kHz), the time to first audio, 50 start and stop cycles with a stable number of open file descriptors, and abort.
  - A muted input device fails with `MicrophoneMutedException`, skipped when the device has no mute property.
  - The test host's microphone grant belongs to the terminal or Rider, so these tests need that grant, not the app's.
- **By hand, with the dev bundle and its Debug log** (`Push-to-talk {Signal}`, which names no key):
  - Hold right Command → Pressed, then Released. Another key during the hold → Cancelled.
  - Hold through Ctrl+Cmd+Q → Cancelled within 1 s. A switch to another user during a hold → Cancelled.
  - A password field takes focus during the hold, then release → the "reads as up" reset.
  - fn with "Press 🌐 key to" set to "Do Nothing" → Pressed and Released.
  - The editor records right Command and fn with Mac names and shows the fn hint.
  - A fresh start without the grant → no notification. A revoke while running → the revoke notification. The grant again through **Set up Pisum Transcribe…** → the relaunch, then Pressed.
  - A deliberately slow handler (a temporary `Thread.Sleep` in a local build) → `CGEventTap timeout!` in the log, and the hotkey still works afterwards.
  - Quit while holding the hotkey → the app ends within the budget.
  - AirPods as input: the time to first audio from the hardware test is below 3 s. A switch to AirPods during the 2 s test continues without an error.
  - Karabiner-Elements, if installed: a key remapped to right Command → Pressed.

*Rejected:* a dev-only **Test hotkey** menu item. It would ship in the app, and #20 brings the real feedback.

### D11: The helper's ABI stays 2

The shell design and setup's Non-Goals expected this change to raise the ABI after setup's 2. With D2, it adds no helper function, so `pisum_abi_version` and `MacNativeLibrary.ExpectedAbiVersion` stay 2. It uses `pisum_microphone_status` as setup defined it.

## Risks / Trade-offs

- **[`CGSSessionScreenIsLocked` is undocumented and could disappear]** → the hardware check covers it. If it disappears, the key reads as missing, so the lock falls back to the release that the physical key state sees. The fallback is the distributed `com.apple.screenIsLocked` notification, which is C as well.
- **[The revoke doesn't end the hook with `ErrorAxApiRevoked`, for example if the tap only goes silent]** → the check by hand revokes while running. If the tap goes silent instead, `IHookAccess` gets the same report from a poll of `AXIsProcessTrusted()` while the hook runs, and D4's rest stays.
- **[AudioQueue doesn't convert to 16 kHz for some device]** → the 32,000-sample hardware test fails loudly. The fallback is capturing at the device rate and resampling in the session, as NAudio does on Windows.
- **[fn arrives through `flagsChanged` and SharpHook doesn't report its release]** → the check by hand. If it fails, fn is left out of `HotkeyKeyNames.MacOS`'s valid keys, and the fn hint goes with it.
- **[A slow UI thread stalls the tap through libuiohook's main-queue dispatch]** → the existing rule against blocking the UI thread, and the timeout check (D9).
- **[A dev Mac keeps right Ctrl as its hotkey]** → accepted (D5). The editor changes it.
- **[A denied microphone isn't checked by hand in the app in this change]** → covered by the unit and integration tests. The check by hand comes with #20, which first opens the microphone in the app.

## Migration Plan

There is no macOS release yet. Windows keeps its default hotkey, its behavior and its settings. The ABI doesn't change. Rollback is reverting the change.

## Open Questions

- Does a re-grant after a revoke work in the same process? It changes nothing here, because D4 relaunches either way. The check by hand records the answer for #20.
