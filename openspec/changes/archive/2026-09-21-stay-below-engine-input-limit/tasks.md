## 1. Reproduce in tests

- [x] 1.1 In `TranscribeCppTranscriberTests`, add `MaxInputDuration_NativeEngineReports400Seconds_Is399Seconds` next to `MaxInputDuration_NativeEngineReportsNoLimit_Is400Seconds`, in the same shape:
  1. Set `_engineFactory.MaxAudio` to 400 s.
  2. Load on `BackendPreference.Cpu`.
  3. Assert that `_sut.MaxInputDuration` is 399 s.

  Verify: the test fails today with 400 s.
- [x] 1.2 In `TranscribeCppTranscriberHardwareTests`, add the explicit test `TranscribeAsync_AudioOfMaxInputDuration_ReturnsResult` (design D4). For each model from `HardwareTestAssets.InstalledModelsOrSkip()`:
  1. Load it on `BackendPreference.Cpu` and assert the status is `Ready`.
  2. Transcribe `new float[SampleAccumulator.MaxCountFor(_sut.MaxInputDuration)]` of silence as an English transcription (`TranscriptionTask.Transcribe`, `"en"`, `"en"`).
  3. Report the model id, `MaxInputDuration` and the elapsed time with `TestContext.Current.SendDiagnosticMessage`.

  The test passes if every call returns, with any text, even empty or truncated.

  Verify: run it with `dotnet test Pisum.Transcribe.slnx --filter-method "*.TranscribeAsync_AudioOfMaxInputDuration_ReturnsResult" --explicit on`. It should fail today with a `TranscriptionFailedException` whose status is `ErrInputTooLong`. If no catalog model is installed, the test is skipped; say so rather than counting it as a reproduction.

## 2. Fix

- [x] 2.1 In `TranscribeCppTranscriber.TryPublishReady`, set `_maxInputDuration` to `engine.MaxAudio` minus a 1 s margin when the engine reports a limit, and keep `DefaultMaxInputDuration` when it reports none (design D1–D3).
  - Put the margin in a private constant field next to `DefaultMaxInputDuration`.
  - Add a comment explaining why: transcribe.cpp 0.2.3 rejects input of exactly the limit it reports for canary, because of an off-by-one in the frame count.
  - Leave `TranscribeCppEngineFactory` and `INativeSpeechEngine.MaxAudio` unchanged.
  - Check that the XML docs of `ITranscriber.MaxInputDuration` and `DefaultMaxInputDuration` still fit, and change them only if they don't.

  Verify: 1.1 passes, and `MaxInputDuration_NativeEngineReportsNoLimit_Is400Seconds` still passes.
- [x] 2.2 Adjust `TranscribeAsync_AudioLongerThanModelMaximum_IsRejectedWithoutRunningModel` to match the unchanged spec scenario "Audio above model limit":
  - Set `_engineFactory.MaxAudio` to 401 s, so the maximum is 400 s.
  - Keep the 401 s request and the assertion that the exception's `MaxInputDuration` is 400 s.

  Verify: the test passes.
- [x] 2.3 In `TranscribeAsync_AutoOnVulkanBackendErrorOnLongestClip_RetriesOnCpuAndReturnsText`, change the clip from 400 s to 399 s, the longest clip the fake's default 400 s limit now allows. That means the request, the `AudioDuration` assertion and both expected sample counts. Verify: the test passes. Without the change it fails with `AudioTooLongException`.

## 3. Verification

- [x] 3.1 Run `dotnet build Pisum.Transcribe.slnx`, `dotnet test Pisum.Transcribe.slnx` and `openspec validate stay-below-engine-input-limit --strict`. Verify:
  - The build has no warnings.
  - All tests pass, including `DictationControllerTests`, which checks that the recorder starts with the transcriber's `MaxInputDuration`, and the tests for the return to Vulkan after an out-of-memory error.
  - Validation reports no issues.
- [x] 3.2 Run the Hardware test from 1.2 again with `--explicit on`. Verify: it passes for every installed catalog model with a maximum input duration of 399 s. Record which of the three catalog models ran and how long each transcription took.
- [x] 3.3 Manual end-to-end check of the issue's steps. It takes about 7 minutes.
  1. Start the app with `dotnet run --project src/Pisum.Transcribe` and `canary-1b-v2-q8_0`.
  2. Clear **Trim silence before transcription** in **Settings…**.
  3. Focus a text editor and hold the push-to-talk hotkey until the recording stops by itself. Speak, or play speech, the whole time, so there is text to insert.

  Verify:
  - The overlay timer stops at 6:39, and the notification "Maximum dictation length reached" appears.
  - Text is inserted, and no "Dictation failed" notification appears.
  - The log has no `The dictation ended with TranscriptionFailedException` entry.
