## Context

See proposal.md, Why. On **Exit**, `ShutdownCoordinator` removes the tray icon, awaits `host.StopAsync()` without blocking the UI thread, disposes the host and then shuts down the WPF application. The host stops hosted services one at a time in reverse registration order, so the settings window services stop first, then `DictationController`, then `DictationFeedback`, and after them the text inserter, the hotkey hook, the transcription engine and the model services.

`DictationController.StopAsync` cancels `_stopping`, completes the event channel and waits for the loop and the processing task. The loop returns on the cancellation, so a dictation that is still running is never ended:

| Phase at exit | Microphone | Overlay after exit | Why |
|---|---|---|---|
| Starting (grey dot) | released: the cancelled start releases its session | stays in the starting look | the start's cancellation reaches the loop's catch, which returns without ending the dictation |
| Recording | open until `AudioRecorder.DisposeAsync` runs at host disposal | stays, "Recording" keeps counting | nothing aborts the recorder, and the overlay's timer keeps running on the free UI thread |
| Transcribing | already closed | stays, spinner and "Transcribing…" | `ProcessAsync` posts `ProcessingCompleted` into the completed channel, where it is dropped, so `EndDictation` never runs |
| Short message | already closed | hides after 1.5 s | the message timer |

The overlay closes only when `Application.Shutdown` closes all windows, after the host has been disposed.

## Goals / Non-Goals

**Goals:**
- Each dictation service ends the part of the dictation that it owns: the controller the recording, the feedback the overlay.
- The microphone is released and the overlay hidden early in `host.StopAsync()`, before the transcription engine unloads.

**Non-Goals:**
- Windows sign-out, shutdown or restart. WPF's own `WM_QUERYENDSESSION` handling raises `SessionEnding` and then calls `Application.Shutdown()`, so `ShutdownCoordinator` never runs and no hosted service is stopped. Routing that through the coordinator is issue #13.
- Changing `ShutdownCoordinator` or the order in which hosted services stop.

## Decisions

### D1: The controller aborts a running recording after its loop has ended

`DictationController.StopAsync` keeps its current order: unsubscribe, cancel, complete the channel, await the loop and the processing task. After that it is the only code that touches `_state`, so it reads it without a race. If the state is `Recording`, it awaits `_recorder.AbortAsync()`, which releases the microphone and discards the audio. After `AbortAsync` returns, it writes one Information log entry with the recording's duration. Written after the release, the entry also tells a slow exit apart: if the release hangs, the entry is missing and the watchdog's error follows.

A hotkey cancel logs its abort only at Debug. The exit abort is logged at Information anyway, because it is the only abort that runs during the shutdown, where a hanging release would otherwise look like any other slow exit.

`AbortAsync` covers every recorder state that the controller can see as `Recording`. If the maximum duration was reached or the microphone was lost and the event is still unhandled in the channel, `AbortAsync` finds the session already released and only resets the recorder. It cannot find the recorder `Starting`, because the loop awaits every start before it ends.

- *Alternative: post a "stopping" event, handled like `HotkeyCancelled`, from `IHostApplicationLifetime.ApplicationStopping`.* The controller is already the fourth hosted service to stop, and the three before it (the settings window services) only unsubscribe and return at once, so the event would not end the dictation noticeably earlier. It would add an event kind that races with the cancellation of `_stopping`.

### D2: The feedback hides the overlay when it stops, without rendering the tray

`DictationFeedback.StopAsync` unsubscribes from `StatusChanged` as today, then queues on the UI thread: end any short message (`EndMessage`), then hide the overlay if it was created. It does not call `Render`. It doesn't create the overlay just to hide it.

This covers every phase in the table, including the starting look and "Transcribing…", which the controller does not see as a running recording. The host stops `DictationFeedback` right after `DictationController`, so no `Show*` call follows the hide. `Show*` calls that the controller queued before it stopped run first, because the dispatcher runs queued work in order. `EndMessage` makes a message timer that fires later do nothing.

- *Alternative: the controller calls `EndDictation()` in `StopAsync`.* `ShowIdle` runs `SetPhase` → `Render` → `SetStatus` before `Overlay.Hide()`, and for **Exit** `ShutdownCoordinator` has already disposed the `TaskbarIcon`. In H.NotifyIcon 2.4.1, setting `Icon` on a disposed `TaskbarIcon` throws `ObjectDisposedException` from `TrayIcon.UpdateIcon`, and so does `ShowNotification`. The icon is not created again. The action runs through `Dispatcher.InvokeAsync`, which stores the exception in the operation's task instead of raising `Dispatcher.UnhandledException`, so nothing crashes, but the rest of the action is skipped: the overlay would stay visible. It would also miss the starting look, where the controller's state is still `Idle`.
- *Alternative: `ShutdownCoordinator` ends the dictation.* Hosting would then depend on Dictation, against the feature-folder layout.

### D3: `StopAsync` of both services keeps ignoring its cancellation token

The abort awaits the release of one capture session, which is the same work `AudioRecorder.DisposeAsync` does today at host disposal, only earlier. `ShutdownCoordinator`'s watchdog still ends the process after 4.5 s if a release hangs.

## Risks / Trade-offs

- [A capture session whose stop hangs now delays the stop of the services after the dictation controller, the transcription engine among them. `DictationFeedback` is one of them, so the overlay also stays until the watchdog ends the process. The release has no timeout: `WasapiCaptureSession.StopAsync` waits for NAudio's `RecordingStopped`.] → The same session already delays host disposal today, and the watchdog ends the process after 4.5 s. The recorder logs a failed stop or release as a warning, and the missing abort entry (D1) shows the hang in the log. Hiding the overlay before the abort is not worth it: stopping the feedback first, or not awaiting the abort, lets a later `Show*` call show the overlay again, for a case the watchdog already bounds. This is also why the spec says "at once" and gives no number.
- [A `Show*` call that the controller queued just before it stopped can run after the tray icon was removed. Its `Render` then throws `ObjectDisposedException` and skips the overlay update in the same action. This race exists today.] → D2's hide is queued later, so the overlay still hides. The exception stays in the dispatcher operation's task, and it is logged as an unobserved task exception only if that task is collected before the process ends. A guard in `TrayIconService` would only avoid that log entry, so this change does not add one.
- [The overlay hide is queued, not awaited, so `DictationFeedback.StopAsync` can return before the overlay is hidden.] → The UI thread is free while `ShutdownCoordinator` awaits the host, so the hide runs within milliseconds, long before `Application.Shutdown`.
- [Sign-out still leaves the microphone open until the process ends.] → Out of scope here, tracked in #13. WPF closes the overlay with all other windows in that case.
