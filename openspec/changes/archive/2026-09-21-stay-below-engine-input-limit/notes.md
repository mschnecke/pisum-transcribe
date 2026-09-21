# Notes

## Maximum input on the real engine (tasks 1.2 and 3.2), 2026-09-21

**Machine:** Intel Arc 140T iGPU, as the Vulkan log reports it. transcribe.cpp 0.2.3. Installed catalog models: `canary-1b-v2-q8_0` and `canary-180m-flash-q8_0`. `canary-1b-v2-q4_k_m` was not installed, so it did not run.

**Test:** `TranscribeAsync_AudioOfMaxInputDuration_ReturnsResult`, CPU backend, silence of `SampleAccumulator.MaxCountFor(MaxInputDuration)` samples, English transcription. Run with `Pisum.Transcribe.Tests.exe -explicit only -method "*.TranscribeAsync_AudioOfMaxInputDuration_ReturnsResult" -diagnostics`. `dotnet test` does not print the diagnostic messages, not even with `--xunit-diagnostics on`.

### Before the fix (task 1.2)

The first model, `canary-1b-v2-q8_0`, failed with `TranscriptionFailedException`, status `ErrInputTooLong`, 400 s maximum input:

```
canary run: input too long — 5001 encoder frames exceed the 5000 the model supports (~400 s max). See transcribe_capabilities.max_audio_ms.
```

### After the fix (task 3.2)

| Model | Maximum input | Outcome | Transcription time |
|---|---|---|---|
| `canary-1b-v2-q8_0` | 399.000 s | completed | 305.84 s |
| `canary-180m-flash-q8_0` | 399.000 s | completed | 73.35 s |

The times are for silence on the CPU backend, and they are not a benchmark of real dictations.
