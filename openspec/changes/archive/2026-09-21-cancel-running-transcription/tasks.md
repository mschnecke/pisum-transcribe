## 1. Engine: specify the caller cancellation

- [x] 1.1 Add unit tests to `TranscribeCppTranscriberTests` for the requirement "Request cancelled by the caller", without changing `TranscribeCppTranscriber` (design, Context). Verify these tests pass:
  - `TranscribeAsync_CallerCancelsRunningRequest_EndsCancelledAtOnceAndAbortsRun`: a fake run that blocks until its token is cancelled. After the caller cancels, `TranscribeAsync` ends as cancelled, the fake run saw its token cancelled, and the status stays `Ready`.
  - `TranscribeAsync_CallerCancelsRunThatReturnsLate_NextRequestRunsAfterIt`: a fake run that ignores cancellation until it is released. After the caller cancels, the caller is released at once, a second request doesn't start its run before the first run returns, and it completes once the first run is released.
  - The existing `TranscribeAsync_QueuedRequestCancelled_EndsCancelledAtOnceWithoutRunning` and `TranscribeAsync_CallerCancelsDuringCpuReloadAfterVulkanBackendError_EndsCancelledWithoutRetry` still pass and cover the other two scenarios.

## 2. Controller: cancel a dictation

- [x] 2.1 In `DictationController`, create a linked cancellation token source per dictation when `StopRecordingAsync` moves to `Processing`, pass its token to `TrimSilence`/`DetectSpeech` and `TranscribeAsync` (not to `InsertAsync`), and dispose it when the loop handles `ProcessingCompleted` (design D1). Change the `OperationCanceledException` filters in `ProcessAsync` and `TrimSilence` to the dictation token. Verify the existing controller tests, including `StopAsync_DuringSpeechDetection_CancelsDetectionWithoutTranscribing`, still pass.
- [x] 2.2 Add the event kind `CancelRequested`, handled only in `Processing` by calling `CancelAsync` on the dictation's source (design D2). Subscribe to `IDictationFeedback.CancelRequested` in `StartAsync` and unsubscribe in `StopAsync`. When `ProcessAsync` ends through a cancel while `_stopping` is not cancelled, write one Information log entry with the processing and audio durations (design D4). Update the class `<remarks>` to mention the cancel. Verify these new unit tests pass:
  - `CancelRequested_DuringTranscription_ShowsIdleWithoutInsertOrNotification`: the transcriber sees its token cancelled, `ShowIdle` is called, and `InsertAsync`, `Notify` and `ShowNoSpeech` are not called.
  - `CancelRequested_DuringSpeechDetection_ShowsIdleWithoutTranscribing`: the detector sees its token cancelled, `TranscribeAsync` is not called, and no "detection failed" warning is logged.
  - `CancelRequested_ThenPressed_StartsNewRecordingWithoutBusy`: after the cancel, the next press starts the recorder and doesn't call `ShowBusy`.
  - `CancelRequested_WhileIdleOrRecording_IsIgnored`: a cancel between dictations or during a recording changes nothing, and a later release still transcribes and inserts.
  - `CancelRequested_AfterTranscriptReady_InsertsText`: a cancel posted while `InsertAsync` is running doesn't cancel it, and the text is inserted.
  - `CancelRequested_DuringTranscription_LogsDurationsWithoutTranscript`: the log has the cancel entry with durations and no text.

## 3. Feedback: the tray menu item

- [x] 3.1 Add `DictationMessages.CancelTranscriptionMenuItem` ("Cancel transcription") and the event `CancelRequested` on `IDictationFeedback`. In `DictationFeedback.StartAsync`, add the menu item on the UI thread, visible when the phase is `Transcribing`, raising `CancelRequested` when chosen (design D3). Update `FakeDictationFeedback` so controller tests can raise the event. Verify these new unit tests pass:
  - `StartAsync_AddsCancelMenuItem_VisibleOnlyWhileTranscribing`: the `isVisible` callback returns false when idle and after `ShowRecording`, true after `ShowTranscribing`, and false again after `ShowIdle`.
  - `CancelMenuItem_Chosen_RaisesCancelRequested`: invoking the item's `onClick` raises the event once.

## 4. Measure the encoder delay

- [x] 4.1 Add the `Hardware` test `Run_CancelledEarlyOnCpu_ReportsTimeToReturn` to `TranscribeCppBenchmarkTests` (design D5): load the default model on the CPU backend, build 60, 120 and 399 s clips by repeating the German clip (400 s exceeds the model's 5000 encoder frames), cancel each run 100 ms after it starts, and report the time until `Run` returns as a table in the diagnostic messages. Mark it `[Fact(Explicit = true)]`. Verify: the default test run skips it, and `Pisum.Transcribe.Tests.exe -explicit only -class "*.TranscribeCppBenchmarkTests" -diagnostics` prints the table.
- [x] 4.2 Run the test on the target laptop and add the measured times to the first risk in `design.md`. Verify: the risk states the time to return for each clip length.

## 5. Verification

- [x] 5.1 Run `dotnet build Pisum.Transcribe.slnx`, `dotnet test Pisum.Transcribe.slnx` and `openspec validate cancel-running-transcription --strict`. Verify: the build has no warnings, all tests pass, and validation reports no issues.
- [ ] 5.2 Check by hand with `dotnet run --project src/Pisum.Transcribe`, backend `cpu` and an installed model (spec scenarios "Cancel a long transcription on the CPU", "Item hidden outside a transcription" and "Press right after a cancel"). Open the tray menu between dictations and during a recording, and verify that **Cancel transcription** is not shown. Dictate about 60 s, open the tray menu during "Transcribing…" and choose **Cancel transcription**. Verify: the overlay hides at once, nothing is inserted, no notification appears, the tray shows "Ready (CPU)", and the next press starts a recording. The log has the cancel entry with durations only.
