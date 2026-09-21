## Why

A dictation that runs to the maximum length is lost (issue #14). The recording stops at exactly the limit the speech engine reports, 400 s for every catalog model. The engine then rejects a clip of exactly that length as too long: "5001 encoder frames exceed the 5000 the model supports". The dictation ends with "Transcription failed." and nothing is inserted, although the dictation spec and the notification promise that the recording is transcribed and inserted. A clip of 399 s is accepted.

The cause is an off-by-one in transcribe.cpp 0.2.3. For canary models, the advertised `max_audio_ms` is 400,000 ms, but the length check counts one more mel frame than that calculation assumes. The longest input the engine accepts is 6,399,999 samples, one sample short of 400 s. The bug is still present on upstream `main`, and the package version is pinned, so the application has to stay below the reported limit itself.

## What Changes

- The transcriber's maximum input duration becomes 1 second less than the longest input the native engine reports. For every catalog model, that is 399 s instead of 400 s.
  - Recording stops at the new maximum, because the dictation already starts the recorder with the transcriber's maximum input duration.
  - The "audio too long" check and its error use the new maximum.
  - A dictation that runs to the maximum length is transcribed and inserted again. The overlay timer stops at 6:39 instead of 6:40.
- If the engine reports no limit, the maximum input duration stays 400 s, as it is today.
- The native engine adapter keeps reporting the engine's value unchanged. Only the transcriber applies the margin.
- Not included:
  - Splitting long audio into chunks.
  - Changing how the engine's "input too long" error is classified or worded.
  - Reporting the off-by-one to transcribe.cpp. A draft exists for the user to post.
  - The claim in upstream PR #112 that Canary 1B v2 can silently drop speech from clips longer than 40 s. It is unverified and needs its own issue.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `transcription`: requirement "Audio input contract" states how the maximum input duration is derived from the engine's limit, and gets the scenarios "Audio at the model limit" and "Engine reports no limit".

## Impact

- Code: `src/Pisum.Transcribe/Transcription/TranscribeCppTranscriber.cs`, where the model becomes ready and the maximum input duration is published. `DictationController`, `AudioRecorder` and `TranscribeCppEngineFactory` stay unchanged.
- Tests:
  - `TranscribeCppTranscriberTests`: one new unit test, and two existing tests that depend on the 400 s maximum are adjusted.
  - `TranscribeCppTranscriberHardwareTests`: one new explicit Hardware test that transcribes exactly the maximum input duration with every installed catalog model.
- No new dependencies, settings or UI.
- User-visible: the maximum dictation length is 6:39 instead of 6:40, and a dictation that reaches it is transcribed and inserted.
- Affects every backend (CPU and Vulkan) and all three catalog models, because the engine checks the length before any backend work.
- Relates to issue #14. Found while benchmarking for #11.
