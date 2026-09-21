## 1. Abort the recording at exit

- [x] 1.1 In `DictationController.StopAsync`, after the loop and the processing task have ended, await `_recorder.AbortAsync()` if `_state` is `Recording`. After it returns, write one Information log entry with the recording's duration in seconds (design D1). Update the method's `<summary>` to say that it aborts a running recording. Verify: a new unit test `StopAsync_WhileRecording_AbortsRecording` records with `RecordAsync()`, calls `StopAsync`, and asserts that `AbortAsync` was called once, `StopAsync` (the recorder's) and `TranscribeAsync` were not called, no notification was shown, and the log has the entry without any audio or text.
- [x] 1.2 Cover the states that must not abort (design D1). Verify each new unit test passes:
  - `StopAsync_WhileIdle_DoesNotAbortRecording`: `StopAsync` without a press → `AbortAsync` is not called.
  - `StopAsync_DuringSpeechDetection_CancelsDetectionWithoutTranscribing` and `StopAsync_WhileStartPending_CancelsStartAndStops` still pass, and a check that `AbortAsync` was not called is added to both.

## 2. Hide the overlay at exit

- [x] 2.1 In `DictationFeedback.StopAsync`, after unsubscribing from `StatusChanged`, queue on the UI thread: `EndMessage()`, then `_overlay?.Hide()`, without calling `Render` and without creating the overlay (design D2). Update the class `<remarks>` to say that stopping hides the overlay and leaves the tray alone. Verify these new unit tests pass:
  - `StopAsync_WhileRecording_HidesOverlayWithoutTrayUpdate`: after `ShowStarting` and `ShowRecording`, `StopAsync` hides the overlay once and adds no `SetStatus` call.
  - `StopAsync_WhileTranscribing_HidesOverlay`: the same after `ShowTranscribing`.
  - `StopAsync_WhileMessageShown_HidesOverlayAndIgnoresMessageTimer`: after `ShowTranscribing` and `ShowBusy`, `StopAsync` hides the overlay. Advancing the fake time by `MessageDuration` afterwards shows nothing again (no further `ShowTranscribing` or `Hide`).

## 3. Verification

- [x] 3.1 Run `dotnet build Pisum.Transcribe.slnx`, `dotnet test Pisum.Transcribe.slnx` and `openspec validate end-dictation-at-exit --strict`. Verify: the build has no warnings, all tests pass, and validation reports no issues.
- [x] 3.2 Check by hand with `dotnet run --project src/Pisum.Transcribe` and an installed model (spec scenarios "Exit during a recording" and "Exit during transcription"). Hold the hotkey until the overlay shows "Recording", then choose **Exit** from the tray while still holding it. Verify: the overlay and the Windows microphone-in-use indicator disappear at once, nothing is inserted, and the log has the abort entry followed by the shutdown. Repeat during "Transcribing…" and verify that the overlay disappears at once.
