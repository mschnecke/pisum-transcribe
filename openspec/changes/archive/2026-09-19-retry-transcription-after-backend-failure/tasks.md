## 1. Caller cancellation

- [x] 1.1 In `TranscribeCppTranscriber.TranscribeAsync`, await `item.Completion.Task.WaitAsync(cancellationToken)`, as `LoadAsync` does (design D4). Verify: a new unit test blocks a run in the fake engine, queues a second request, and cancels only the second request's token. The second request ends as cancelled before the blocked run is released, and the fake engine never receives it (`Runs` has one entry after the release).

## 2. Retry on the CPU

- [x] 2.1 Change the backend-error branch of `Run` and `RecoverFromBackendFailure` (design D1–D3). The recovery reports whether the engine is `Ready` on the CPU for its generation. With `auto` on Vulkan, `Run` keeps the request open through the reload, then runs the same item once more on the CPU, or completes it according to D3's table. Add the audio duration to the failure log line and a warning before the retry, with no text or samples in either. Update the class `<remarks>` to say that the failed request runs again on the CPU. Verify: rewrite `TranscribeAsync_AutoOnVulkanBackendError_FailsRequestAndRunsQueuedRequestOnCpu` as `TranscribeAsync_AutoOnVulkanBackendError_RetriesOnCpuBeforeQueuedRequest`. It asserts that the failed request returns the fake CPU text, that its CPU run gets the same samples and options as the failed run, that the queued request completes after it, the status sequence `Loading, Ready, Loading, Ready`, and the calls `... "Run Vulkan", "Dispose Vulkan", "Load Cpu", "WarmUp Cpu", "Run Cpu", "Run Cpu"`. `TranscribeAsync_AutoOnVulkanBackendError_ReloadOnCpuWarmsUpOnOneSecondOfSilence` and `TranscribeAsync_AutoOnVulkanBackendErrorAndDisposeFails_ReloadsOnCpuAndRunsNextRequest` now expect the first request to succeed, and their run and call lists gain the retry. `dotnet build Pisum.Transcribe.slnx` passes.
- [x] 2.2 Add unit tests for the other outcomes (spec "Failures during transcription", design D3). Verify each test passes:
  - A 400 s request fails on Vulkan with `ErrBackend` → it is run again on the CPU and returns its text.
  - The CPU retry fails with `ErrBackend` → `TranscriptionFailedException`, status `Failed` with `BackendFailedMessage`, and exactly one `Run Cpu`.
  - The CPU retry fails with another status → `TranscriptionFailedException`, and the status stays `Ready` with backend `CPU`.
  - The CPU load fails → `TranscriptionFailedException` with status code `ErrBackend` (the Vulkan error), status `Failed` with `LoadFailedMessage`, and no `Run Cpu`.
- [x] 2.3 Add unit tests for the shutdown and cancellation rows of design D3 (spec "Clean shutdown during transcription"). Verify each test passes:
  - `StopAsync` is called while the fake CPU load blocks, and the load is released right after → the failed request ends as cancelled, no `Run Cpu` is made, and `StopAsync` completes within `StopTimeout`. Cover both a CPU warm-up that throws `OperationCanceledException` on the cancelled token and one that ignores the token.
  - The caller's token is cancelled while the fake CPU load blocks → the request ends as cancelled before the load is released. After the release the status becomes `Ready` with backend `CPU`, and no `Run Cpu` is made.
- [x] 2.4 Cover a newer load that replaces the recovery (design D5, spec scenario "Backend change during the failed run"). Verify: `TranscribeAsync_VulkanBackendErrorWhileReloadQueued_SkipsCpuFallbackAndLoadsNewest` still passes unchanged. A new test requests a load with backend `cpu` while the fake CPU reload blocks. After the release, the failed request is rejected with `TranscriptionFailedException`, the reloaded CPU engine is disposed without a `Run Cpu`, and the status ends `Ready` from the newer load.

## 3. Dictation feedback

- [x] 3.1 Add `ShowIdle_EngineReadyAgainDuringDictation_StaysTranscribingThenShowsReadyOnCpu` to `DictationFeedbackTests` (dictation spec scenario "Engine reloads during a dictation"): while transcribing, `Loading` and then `Ready` with backend `CPU` are raised. Keep `ShowIdle_EngineReloadedDuringDictation_ShowsLoadingThenReadyOnCpu`, which still covers a dictation that ends while the engine is loading (design D5). Verify: the tray shows the transcribing icon and "Transcribing…" after both status changes, and "Ready (CPU)" after `ShowIdle`. If the test passes without code changes, `DictationFeedback` needs none.

## 4. Verification

- [x] 4.1 Run `dotnet test Pisum.Transcribe.slnx` and `openspec validate retry-transcription-after-backend-failure --strict`. Verify: all tests pass and validation reports no issues. A real Vulkan device loss can't be triggered on demand, so the fake-engine tests carry the behavior. The explicit hardware tests need no change.
