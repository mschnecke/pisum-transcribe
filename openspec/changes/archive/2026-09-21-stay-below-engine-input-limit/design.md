## Context

See proposal.md, Why. Today the limit passes through the application like this:

```
 transcribe.cpp                 TranscribeCppEngineFactory     TranscribeCppTranscriber           consumers
 ---------------------------    --------------------------     ------------------------------     ------------------------------
 EffectiveMaxAudioMs        --> Engine.MaxAudio            --> TryPublishReady sets           --> DictationController starts the
 400,000 ms for canary          "as the native engine          _maxInputDuration, read            recorder with MaxInputDuration;
                                reports it"                    through MaxInputDuration           TranscribeAsync rejects longer
                                                                                                  audio with AudioTooLongException
```

- `SampleAccumulator.MaxCountFor(400 s)` is exactly 6,400,000 samples, and `TranscribeAsync` rejects only audio *longer* than the maximum. So a recording at the limit reaches the engine.
- Only `TryPublishReady` sets the maximum from a loaded engine. `SetStatusLocked` and `ReleaseEngine` reset it to `DefaultMaxInputDuration` (400 s). The return to Vulkan after an out-of-memory error (#12) keeps the CPU engine's maximum while it reloads, and `TryPublishReady` then sets it again for the same model.
- `ITranscriber.MaxInputDuration` is documented as "the longest audio input the loaded model accepts". Today that is false by one sample.
- Upstream v0.2.3, canary:

| Samples | Mel frames, `floor(n / 160) + 1` | Encoder frames, 3 stride-2 stages | Result |
|---|---|---|---|
| 6,384,000 (399 s) | 39,901 | 4,988 | accepted |
| 6,399,999 | 40,000 | 5,000 | accepted, the longest input |
| 6,400,000 (400 s) | 40,001 | 5,001 | `ErrInputTooLong` |

  `canary_max_audio_ms` computes `5000 × 8 × 160` samples without the centred first frame. The length check runs on the CPU before the encoder graph is built, so every backend is affected. All three catalog models are canary and report 400,000 ms.
- `ErrInputTooLong` is classified as `FailureClass.Other`. It becomes a `TranscriptionFailedException` without a retry on the CPU backend, and the status stays `Ready`.
- `FakeNativeSpeechEngineFactory` accepts any input length, so the unit tests could not see the off-by-one.

## Goals / Non-Goals

**Goals:**
- The transcriber publishes a maximum input duration that the engine really accepts. The recorder cap, the "audio too long" check and its error all follow from that one value.

**Non-Goals:**
- Working out each family's exact frame arithmetic in the application.
- Making the fake engine behave like the native length check.
- Changing the engine adapter, the recorder or the dictation controller.

## Decisions

### D1: The transcriber applies the margin when a model becomes ready

`TryPublishReady` sets the maximum input duration to the engine's reported limit minus the margin, or to `DefaultMaxInputDuration` if the engine reports no limit (`MaxAudio <= 0`). Everything that reads `MaxInputDuration` gets the corrected value, and `ITranscriber`'s documentation becomes true without changing its wording.

- *Alternative: subtract in `TranscribeCppEngineFactory.Engine`.* That class is the only one that references TranscribeCppSharp, so it would be a natural place for native knowledge. But `INativeSpeechEngine.MaxAudio` would stop meaning "as the native engine reports it". The adapter wraps the native library, so the margin could only be tested on hardware.
- *Alternative: pass a shorter duration to the recorder, in `DictationController`.* `MaxInputDuration` would still promise a length that the engine rejects, and there would be two limits to keep apart.
- *Alternative: drop the extra samples just before the native call.* That silently changes what the transcriber does with its input, for a case the recorder can prevent.
- *Alternative: split long audio into chunks.* That is a feature, not a fix for this bug.

### D2: The margin is a fixed 1 second

- 399 s is the length the issue already shows the engine accepting on real hardware.
- It holds even if a family's advertised limit is off by a few frames. Upstream calls these limits "advisory" for some families.
- Users hardly notice it: the overlay timer stops at 6:39 instead of 6:40, and nothing that was recorded is lost.

- *Alternative: 1 sample or 1 tick.* This is exactly enough for canary today, but only because of the exact frame arithmetic in one upstream version.
- *Alternative: one 10 ms mel hop or one 80 ms encoder frame.* These are still specific to canary's frame geometry, with no hardware evidence behind them.

If upstream later corrects `max_audio_ms` to 399,999 ms, the maximum becomes 398.999 s, which is still correct.

### D3: The 400 s default keeps no margin

`DefaultMaxInputDuration` applies while no model is loaded, when nothing reads it, and when the engine reports no limit. Without an engine limit there is nothing to stay under, so it stays 400 s. That also holds if upstream starts chunking canary audio and reports it as unbounded (PR #112 proposed this). A reported limit of 1 s or less is not handled, because no catalog model comes close.

### D4: The real engine is checked only by an explicit Hardware test

The unit tests check the arithmetic with the fake engine: 400 s reported gives 399 s, and no limit gives 400 s. A new explicit Hardware test transcribes `SampleAccumulator.MaxCountFor(MaxInputDuration)` samples, exactly what the recorder captures at the limit, with every installed catalog model:
- on the CPU backend, because the length check does not depend on the backend, and CPU avoids out-of-memory fallbacks that would blur the result
- on silence, the same input as the CPU warm-up
- passing if the call returns, even with empty or truncated text

- *Alternative: have the fake engine reject input as long as its limit.* Then a default test run would catch this kind of bug. But the fake would copy one upstream quirk as our guess, and it would go wrong without anyone noticing once upstream fixes the boundary.

## Risks / Trade-offs

- [The default test run cannot see the native boundary.] → Run the explicit Hardware test whenever the TranscribeCppSharp packages or the catalog models change.
- [A future family's advertised limit is off by more than 1 s.] → The Hardware test fails for that model once it is installed. The margin can then be revisited.
- [Two existing unit tests rely on exactly 400 s passing with the fake's default 400 s limit.] → They are adjusted in this change, see tasks.md.
- [Dictations lose 1 s of maximum length.] → Accepted. A dictation that reaches the limit was lost completely before this change.

## Migration Plan

None. Nothing is persisted: the maximum is computed each time a model loads. Rollback reverts the one assignment in `TryPublishReady`.
