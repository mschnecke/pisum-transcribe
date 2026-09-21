## Why

With backend `auto`, a backend error during a transcription on Vulkan (a lost GPU device after a driver reset or sleep, or the GPU running out of memory) makes the engine reject that request with "Transcription failed." before it reloads the model on the CPU. For a dictation, the user's audio is lost and they have to dictate again. A dictation can hold up to 400 seconds of speech, so the loss grows with its length. `add-transcription-engine` chose to reject the request (design D10, "Backend failure"), and this change reverses that decision (issue #9).

## What Changes

- With backend `auto`, when a transcription fails on Vulkan with a backend error, the engine reloads the model on the CPU as today, then runs the failed request once more on the CPU, before the requests queued behind it, and returns its result.
- If that run on the CPU also fails with a backend error, the request is rejected with "transcription failed" and the status becomes `Failed`, as today for a backend error on the CPU. There is no second retry.
- If the reload on the CPU fails, or a newer load requested in the meantime replaces it, the request is rejected with "transcription failed", as today.
- If the application exits during the reload, the request ends as cancelled, and the caller is released as soon as its own cancellation is requested instead of when the reload has finished. The process still ends within 5 seconds.
- The retry runs whatever the clip length. On the CPU, the 1B model runs slower than real time, so a long dictation keeps showing "Transcribing…" for minutes before its text arrives.
- Backends `vulkan` and `cpu`, and errors that are not backend errors, behave as today.
- Not included: retrying errors that are not backend errors, retrying more than once, a notification about the switch to the CPU, and cancelling a running transcription.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `transcription`: "Failures during transcription" retries the failed request on the CPU after the reload instead of rejecting it. "Clean shutdown during transcription" covers an exit during that reload.
- `dictation`: the "Tray icon states" scenario "Engine reloads during a dictation" changes, because the dictation now stays in the transcribing state through the reload and ends with the engine already `Ready` on the CPU.

## Impact

- Code: `src/Pisum.Transcribe/Transcription/TranscribeCppTranscriber.cs` (`TranscribeAsync`, `Run`, `RecoverFromBackendFailure`) and its unit tests on the faked `INativeSpeechEngine` seam. No new dependencies, settings or UI.
- User-visible: after a Vulkan backend error, the dictation's overlay stays on "Transcribing…" and the tray stays in the transcribing state until the text arrives from the CPU, and the hotkey shows "Still processing…" until then.
- Privacy: the queued request already holds the samples in memory for its lifetime. Nothing is written to disk or logs.
- Depends on `add-transcription-engine` and `add-dictation-workflow`. Relates to issues #3 and #6.
