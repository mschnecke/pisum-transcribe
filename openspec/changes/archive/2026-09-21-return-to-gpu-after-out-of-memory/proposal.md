## Why

With backend `auto`, when a transcription runs out of GPU memory on Vulkan, the engine switches to the CPU and stays there until the app restarts or the model or backend setting changes. The memory a Vulkan run needs grows with the clip length (`retry-transcription-after-backend-failure` design, Context). So one long dictation makes every later dictation, even a short one, run on the CPU, which is about eight times slower with the default model (issue #12).

## What Changes

- With backend `auto`, after a transcription fails on Vulkan with an out-of-memory error (`ErrOom`) and has run again on the CPU (#9), the engine reloads the model on Vulkan, so later dictations run on the GPU again.
- The engine returns to Vulkan only if the failed clip was longer than every clip that has completed on Vulkan since the current model and backend setting were loaded, and longer than the 10 s warm-up. An out-of-memory error on a clip no longer than one that has already completed is not caused by the length, so the engine stays on the CPU, as it does today.
- After a return, if the engine runs out of memory again before any clip has completed on Vulkan, it stays on the CPU, as today. If GPU memory stays short for another reason, this limits the cost to one extra round of reloads.
- The return reloads the same model on Vulkan, which takes about 3–5 s. Unlike a reload after a settings change, it keeps the status `Ready`. Requests that arrive during it wait for it and then run on Vulkan, and a hotkey press starts a recording as usual. That way, a dictation that starts after a cancel (#11) isn't rejected as still loading.
- The engine also returns to Vulkan if the caller cancelled the failed request while the engine reloaded on the CPU or while the request ran there.
- The first task measures on the target laptop which clip lengths fail on Vulkan and with which status, and whether the maximum input length is the same on both backends. A checkpoint decides how the work continues. If no clip fails, the measurement is repeated with a forced GPU allocation limit, so the change can still be verified for GPUs with less memory. If long clips fail differently, for example with a lost device or a crash, implementation stops and the design is revised.
- Other backend errors, such as a lost GPU device, behave as today. So do an out-of-memory error during load or warm-up and the backends `vulkan` and `cpu`.
- Not included: a notification about the switch to the CPU, sending long clips straight to the CPU, splitting long clips at pauses, and changes for other backend errors.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `transcription`: "Model stays loaded" and "Failures during transcription" add the return to Vulkan after an out-of-memory error and its two checks. "Request cancelled by the caller" covers the return after a cancel.
- `dictation`: the scenarios "Engine reloads during a dictation" (in "Tray icon states") and "Cancel while the engine reloads on the CPU" (in "Cancel a transcription") now cover only other backend errors. Each gets an out-of-memory counterpart that ends with "Ready (Vulkan)". A new scenario in "Cancel a transcription" covers a dictation that starts while the engine returns to the GPU after a cancel.

## Impact

- Code: `src/Pisum.Transcribe/Transcription/TranscribeCppTranscriber.cs` (`Classify`, `Run`, `RecoverFromBackendFailure`, and how the worker handles loads), and its unit tests on the faked `INativeSpeechEngine` seam. A new `Hardware` test in `TranscribeCppBenchmarkTests`. No new dependencies, settings or UI.
- User-visible: after an out-of-memory error on a long dictation, the text arrives from the CPU as today. The tray keeps showing "Ready (CPU)" for about 3–5 s, then "Ready (Vulkan)". A dictation that ends during those seconds waits for the reload and then runs on the GPU. A later long dictation that runs out of memory again pays for the failed Vulkan attempt and both reloads again.
- Privacy: the checks keep clip lengths in memory only. The log gets lengths and backend names, and no text or audio.
- Depends on #9 (`retry-transcription-after-backend-failure`) and #11 (`cancel-running-transcription`). Relates to #3 and #8.
