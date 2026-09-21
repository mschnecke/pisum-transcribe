## Context

This change consumes the services from the earlier changes. Their interfaces stay the same, but `TextInserter` now finishes the clipboard restore after `InsertAsync` returns (D7):
- `ITranscriber` (`Status`, `StatusChanged`, `MaxInputDuration`, `TranscribeAsync`), plus the typed exceptions and `TranscriptionOptionsValidator`, from `add-transcription-engine`.
- `IPushToTalkHotkey` (`Pressed` / `Released` / `Cancelled`, raised on SharpHook's event thread) and `IAudioRecorder` (`StartAsync(maxDuration)`, `StopAsync`, `AbortAsync`, `MaxDurationReached`, `Failed`; state contract in that change's design D5) from `add-push-to-talk-recording`.
- `IForegroundWindowTracker` and `ITextInserter` (`InsertionOutcome`) from `add-text-insertion`.
- `ITrayIconService` (`SetStatus`, `SetToolTip`, `ShowNotification`) and `ISettingsStore` from `scaffold-app-shell`.

`add-transcription-engine` currently sets the tray tooltip directly. This change takes over ownership of the tray state.

## Goals / Non-Goals

**Goals:**
- A race-free controller: hotkey events from the hook thread, recorder events from audio threads and async completions must never interleave badly.
- A controller that is fully unit-testable with fakes and a fake `TimeProvider`.
- Feedback that never disturbs the user's focus.

**Non-Goals:**
- No queuing of dictations while one is processing. Presses are ignored instead, which keeps paste targets unambiguous.
- No transcript history, undo, or editing before insertion.
- No sounds or audio cues.
- No UI localization. UI strings are English in v1.
- No user-visible cancel for an in-flight transcription.

## Decisions

### D1: Single-reader event loop

`DictationController : IHostedService` pushes every input into a `Channel<DictationEvent>`: `HotkeyPressed`, `HotkeyReleased`, `HotkeyCancelled`, `MaxDurationReached`, `RecordingFailed`. One async loop reads the channel and owns all state (`Idle | Recording | Processing`, plus target, start timestamp and last notification times). Each event handler is short, awaiting only recorder start, stop and abort. The long part (transcribe → insert) runs as a tracked background task. When that task starts, the state is `Processing`, so presses that arrive meanwhile are still read promptly, answered with "Still processing…" and dropped. When the task finishes, it posts `ProcessingCompleted`, and the loop returns to `Idle`. The task finishes when `InsertAsync` returns, which is when the text is delivered, not after the clipboard restore (D7). On shutdown, the controller cancels its stopping token, which also cancels a pending `StartAsync`. It then completes the channel and awaits the tracked task, bounded by the engine's cancellation.

The controller reads `ITranscriber.Status` when a press arrives and needs no status events. Within this change, the engine cannot leave `Ready` during a recording: loading starts only from `NotLoaded` or `Failed`, and the backend fallback runs only during a transcription, which is while the controller is `Processing`. `add-settings-window` adds live model reloads. Its design lets a recording that is running during a reload fail at transcription with "The model is still loading.", which the controller shows like any known exception (D2), so the controller needs no status event.

*Why:* one owner of mutable state removes locking, and ordering follows arrival. *Alternative:* `lock` around a state field, which is error-prone with async and three event sources.

### D2: Flow details

**Pressed**, in state `Idle`:
- If the transcriber is not `Ready`: notify with the reason, throttled to 10 s per reason with `TimeProvider`. For `NotLoaded` and `Loading` the reason comes from `DictationMessages`. For `Failed` it is `ITranscriber.FailureMessage`, which says what to do next: download a damaged model again, or restart after the backend stopped working. When `FailureMessage` is `null`, the generic "model failed to load" text from `DictationMessages` is used.
- Otherwise: `target = tracker.CaptureForeground()`, feedback `Starting(target)`, then `await recorder.StartAsync(transcriber.MaxInputDuration, stoppingToken)`, which completes when audio flows, or fails after 3 s with `MicrophoneNotRespondingException`. Then store the start timestamp, log the time from the press to this point at Information level, and give feedback `Recording()`. A start failure → hide the overlay, error notification, and back to `Idle`. Hotkey events that arrive while the start is pending wait in the channel and are handled afterwards.

**Released**, in state `Recording`:
- If elapsed time since the start timestamp < 300 ms: `AbortAsync`, hide the overlay, back to `Idle`. A release during a slow start is handled once the start completes, so its elapsed time is near zero and it is discarded.
- Otherwise: `clip = StopAsync()`, switch to `Processing`, feedback `Transcribing`, and run `ProcessAsync(clip, target, optionsSnapshot, textInsertionSnapshot)`. If `StopAsync` throws a `RecordingFailedException`, because the microphone was lost just before the release: error notification with its message, hide, back to `Idle`.

**ProcessAsync:**
1. `TranscribeAsync`.
2. `text = result.Text.Trim()`. If empty → feedback `NoSpeech` (1.5 s).
3. Otherwise `InsertAsync(text, target, textInsertionSnapshot, stoppingToken)`, which returns once the text is delivered (D7). A fallback outcome → "copied to the clipboard" notification with the mapped reason. `ClipboardUnavailable` → a notification that the text could not be inserted or copied, because the transcript was not delivered.
4. Known exceptions → notification with their message. An unknown exception → a generic "Dictation failed" plus a log entry with the exception and no transcript.
5. `finally`: clear references to the clip and text, feedback `Idle`, post `ProcessingCompleted`.

**Cancelled**, in state `Recording`: `AbortAsync`, hide, back to `Idle`.

**MaxDurationReached**, in state `Recording`: handled like Released, including a `RecordingFailedException` from `StopAsync`, plus the "maximum dictation length reached" notification.

**RecordingFailed**, in state `Recording`: error notification, hide, back to `Idle`. The recorder has already released the microphone, so no `AbortAsync` is needed.

Recorder events that arrive in any other state are dropped. They lost a race with an event the loop already handled, for example a limit reached just as the user released the hotkey, so handling them would show a second notification or call `StopAsync` twice.

The snapshots are taken at the press, when the dictation starts, from `ISettingsStore.Current`: `Transcription` as the options for `TranscribeAsync`, and `TextInsertion` for `InsertAsync`. A settings save during a recording or transcription therefore applies to the next dictation, as `ITextInserter` ("the settings the dictation started with") and `add-settings-window` expect. Elapsed time uses `TimeProvider.GetTimestamp()`.

### D3: `IDictationFeedback` seam

`IDictationFeedback` has `ShowStarting(InsertionTarget)`, `ShowRecording()`, `ShowTranscribing()`, `ShowBusy()`, `ShowNoSpeech()`, `ShowIdle()` and `Notify(string title, string message)`. The production `DictationFeedback` dispatches to the UI thread and drives `RecordingOverlayWindow` and `ITrayIconService`. Unit tests assert feedback calls on a fake.

The tray has one writer. `DictationFeedback` keeps two inputs on the UI thread:
- **Dictation phase** (`Idle | Recording | Transcribing`), set by the `Show*` calls. `Starting` keeps the tray idle, because the recording icon tells the user to speak.
- **Engine status**, with its backend and failure message, from `ITranscriber.StatusChanged`. As in the hosted service today, `ActiveBackend` and `FailureMessage` are read on the thread that raised the event, so the values belong to that change.

After each change, one `Render()` sets the icon and tooltip. A phase other than `Idle` shows recording or transcribing, and `Idle` shows ready or unavailable from the engine status. `ShowIdle()` ends the dictation's hold on the tray, so a status that changed during the dictation, such as a reload on the CPU backend, shows once the dictation ends. `DictationFeedback` reads `ITranscriber.Status` at startup, because `TranscriberHostedService` starts first and its load has already moved the engine to `Loading`.

Tooltips follow the pattern "Pisum Transcribe – <state>", for example "Pisum Transcribe – Ready (Vulkan)" or "Pisum Transcribe – Recording…", so they contain the product name as `app-shell` requires. The unavailable states use "No model installed", "Loading model…" and "Model failed to load".

The tooltip logic from `add-transcription-engine`'s hosted service moves here, and that service keeps only the load orchestration and failure notification.

*Why one writer:* with two writers, a dictation that ends during a reload on the CPU backend would reset the tray to ready while the engine still loads. *Alternative:* the controller forwards engine status changes as feedback calls, which puts tray logic into the controller.

### D4: Overlay window

`RecordingOverlayWindow` is a small rounded pill of about 220×44 DIP, with a grey dot and no text while starting, a red dot and "Recording 0:03" while recording, or a spinner and "Transcribing…". The starting look has no text, so it does not flicker when the start takes less than 200 ms; it tells the user that the press registered, and the red dot tells them to speak.
- **Window properties:** `WindowStyle=None`, `AllowsTransparency=true`, `Topmost=true`, `ShowActivated=false`, `ShowInTaskbar=false`, `Focusable=false`.
- **Extended styles:** in `SourceInitialized`, CsWin32 `SetWindowLongPtr(GWL_EXSTYLE)` adds `WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED`, which gives no activation, click-through, and no Alt+Tab.
- **Lifetime:** it is created once and shown and hidden with `Show()` / `Hide()`, so it has no creation cost per dictation.
- **Placement:** `MonitorFromWindow(target, MONITOR_DEFAULTTONEAREST)` + `GetMonitorInfo` gives the work area in physical pixels. The overlay is positioned bottom center, 48 px above the work-area bottom, using the monitor's DPI. The app manifest declares `PerMonitorV2`.
- **Elapsed time:** updated by a `DispatcherTimer` every 250 ms while recording.

### D5: Tray icons

The four state icons are drawn once at startup instead of shipped as `.ico` files. `DictationIcons` loads the embedded placeholder `Tray/TrayIcon.ico` at 32×32 px and draws an anti-aliased filled dot in its bottom-right corner with `System.Drawing`, which the tray already uses:
- **ready:** the placeholder icon unchanged
- **recording:** a red dot
- **transcribing:** an amber dot
- **unavailable:** a grey dot

Windows scales the icons for the notification area. They live for the application's lifetime. `ITrayIconService.SetStatus(icon, tooltip)` is used as-is.

*Why:* four hand-drawn `.ico` files are binary assets that are hard to make and review in a code change. Drawn icons stay consistent with the placeholder, and real artwork can replace `DictationIcons` later without touching `DictationFeedback`.

### D6: Message catalog

`DictationMessages` is a static class holding all user-facing strings: the reasons for not being ready (the `Failed` reason only as the fallback for a missing `FailureMessage`), the insertion fallback reasons, the clipboard-unavailable message, the maximum length message and the generic failure. The spec's wording lives in one place, and tests reference the constants.

The `NotLoaded` message reads "No speech model is installed yet. Choose **Download model…** in the tray menu to download one or see the download progress." It also covers a download in progress: downloads run only while the setup window is open, and **Download model…** brings that window, with its progress, to the front.

### D7: Clipboard restore in the background

`TextInserter.InsertAsync` from `add-text-insertion` awaits the 750 ms delay and the restore after Ctrl+V. The controller would stay `Processing` for about 0.75 s after the text is visible. The overlay would still show "Transcribing…", and a press in that time would get "Still processing…". Dictating sentence by sentence runs into this often.

Changes in `TextInserter`, with `ITextInserter` unchanged:
- **Returning at the paste:** after `SendPaste`, `InsertAsync` starts the restore without awaiting it, keeps the task as `_pendingRestore`, and returns `Inserted`.
- **Waiting before the next insertion:** every `InsertAsync` first awaits `_pendingRestore`, before any other step, so the next snapshot holds the user's content and not the previous transcript. The wait is at most about 1.75 s: the 750 ms delay plus about 1 s for a busy clipboard. It is usually already over, because the next recording lasts at least 300 ms and a transcription follows. The controller never starts a processing task before the previous one finishes, so insertions don't overlap, and one field is enough.
- **Waiting at shutdown:** `TextInserter` also implements `IHostedService`, and its `StopAsync` awaits `_pendingRestore`. Hosted services stop in reverse registration order, so the dictation controller stops first. The container disposes the clipboard thread of `WpfClipboardService` only after all hosted services have stopped. `TextInserter` is registered like `TranscribeCppTranscriber`: one singleton behind both `ITextInserter` and `IHostedService`.
- **Restore errors:** the restore stays non-cancellable. It catches and logs its own failures, so awaiting it never throws into the next insertion or into shutdown.

*Alternatives:*
- **Accept the busy time after each paste.** This leaves `add-text-insertion` untouched, but the most common way to dictate runs into "Still processing…".
- **A "delivered" callback on `InsertAsync` that hides the overlay at the paste.** It fixes the look but keeps the busy time.
- **Keep the order in the controller.** The controller cannot see when the paste happened without a contract change, and the clipboard's order belongs to the class that owns the clipboard.

## Risks / Trade-offs

- [The 300 ms minimum might swallow very short words ("Ja").] → A 300 ms press that includes the word is rare. The value is one constant, and it can be revisited with real usage.
- [A release while `StartAsync` is pending waits in the channel until the start completes, up to 3 s. After an accidental tap, the microphone can stay open that long, and a microphone that never responds shows its error after the user has let go.] → Accepted. The error is still true, and cancelling the start on release would not prevent a Bluetooth headset from switching to its headset profile. Cancelling would need a separate `Starting` state that reads the channel while the start runs.
- [Drawn tray icons look plainer than designed artwork.] → They differ by a clear coloured dot, and artwork can replace `DictationIcons` later (D5).
- [A `WS_EX_TRANSPARENT` layered window can flicker or show a black background on some GPU drivers when combined with WPF `AllowsTransparency`.] → Verify on the Intel Xe laptop. The fallback is a non-transparent solid pill (`AllowsTransparency=false`) with the same extended styles.
- [Notifications from H.NotifyIcon may be suppressed by Focus Assist / Do Not Disturb.] → The overlay covers the in-flow cases (no speech, busy). Critical errors also update the tray tooltip.
- [Moving tooltip ownership from the engine hosted service to `DictationFeedback` touches code from `add-transcription-engine`.] → A small, explicit task, with the unit tests for tooltips moved along with it.
- [Returning from `InsertAsync` before the restore changes code from `add-text-insertion`.] → The interface stays the same. The restore tests await the pending restore instead of `InsertAsync`.
- [An exit during a pending restore waits for it, up to about 1.75 s.] → The restore runs on its own timer from the paste, in parallel with the other services' shutdown, so it adds at most its remaining time. That fits the 4 s `HostOptions.ShutdownTimeout`.
- [A permanent backend failure during a dictation shows two notifications: "Transcription failed." from the controller and "The speech model stopped working…" from `TranscriberHostedService`.] → Accepted, because it is rare. Suppressing one would need a contract change: the transcriber completes the request before it sets `Failed`, so checking the status right after the exception is a race.
