## Context

`DictationController.ProcessAsync` from `add-dictation-workflow` receives an `AudioClip` (16 kHz mono float) and calls `ITranscriber.TranscribeAsync`. An empty transcript already maps to `ShowNoSpeech`. `StartRecordingAsync` saves the settings at the press in `ActiveDictation`, so a save during a dictation applies to the next one.

`SettingsViewModel` from `add-settings-window` edits a draft through `ObservableObject` section view models. `HasChanges` compares `BuildSettings()` with the saved baseline, and each section's `Rebase` takes values saved elsewhere for fields the user has not edited (`DraftValue.Rebase`). `SettingsApplier` handles only the hotkey, model and backend. VAD needs no applier entry, because each dictation reads the flag at its press.

Silero VAD v6.2.2 (`snakers4/silero-vad`, MIT) ships `silero_vad.onnx` (2,327,524 bytes). The file is byte-identical in v6.2, v6.2.1 and v6.2.2. Its I/O:

| Tensor | Shape |
|---|---|
| input `input` | `[1, 64 + 512]` float: 64 context samples plus a 512-sample window at 16 kHz |
| input `sr` | `[1]` int64 |
| input `state` | `[2, 1, 128]` float |
| outputs | speech probability and the new state |

The upstream repository has an MIT C# example (`examples/csharp`: `SileroVadOnnxModel.cs`, `SileroVadDetector.cs`) implementing exactly this.

## Goals / Non-Goals

**Goals:**
- Trim silence before transcription and skip silent clips, with no user-visible failure modes.
- A pure, unit-tested trimming function. The real model is also testable in the default test run, since it is bundled and CPU-only.

**Non-Goals:**
- No removal or shortening of pauses inside speech. It could hurt translation context.
- No real-time VAD during recording, such as auto-stopping on silence. Push-to-talk already defines the end.
- No GPU (DirectML) execution. The model is tiny, and CPU keeps the dependency lean.
- No tuning UI for thresholds.

## Decisions

### D1: Port the upstream C# example, not a NuGet wrapper

`SileroVadModel` is a port of `SileroVadOnnxModel.cs`. It keeps the context and state across windows, resets per clip, and replaces the jagged arrays with pre-allocated `float[]` buffers and `OrtValue.CreateTensorValueFromMemory` to avoid per-window allocations. `SileroSpeechDetector` is a port of the segment logic in `SileroVadDetector.cs`, with its defaults:

| Parameter | Value |
|---|---|
| threshold | 0.5 |
| negative threshold | threshold − 0.15 |
| min speech | 250 ms |
| min silence | 100 ms |
| speech pad | 0 (padding is applied by the trimmer) |

The port follows the example at the v6.2.2 tag. That version ends a segment still open at the end of the clip at the number of processed windows × 512, which the port keeps. Its `Dispose` no longer disposes the `InferenceSession`, so the port disposes the session itself.

Output: `IReadOnlyList<SpeechSegment(int StartSample, int EndSample)>`. `THIRD-PARTY-NOTICES.md` holds the MIT notices of Silero VAD and of ONNX Runtime, whose `onnxruntime.dll` the app ships from now on. Notices for the dependencies the app already ships (NAudio, SharpHook, TranscribeCppSharp and ggml, H.NotifyIcon, CommunityToolkit.Mvvm, Serilog, Microsoft.Extensions) are out of scope. They belong to the deferred packaging work. The ported types name their upstream origin (`snakers4/silero-vad`, `examples/csharp`) in a `<remarks>` XML doc line. They get no file headers, following the project convention.

*Why not `SileroVad` 1.3.0 or `ManySpeech.SileroVad` 1.1.2:* neither declares a license expression, both add transitive surface, and the upstream example is small and official.

### D2: Bundled model file

The model is `src/Pisum.Transcribe/VoiceActivity/Assets/silero_vad.onnx`, taken from the v6.2.2 tag, commit `60b7ffa243625ebdc1070275a29f18c87843786a`, with SHA-256 `1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3`. It is copied to the output (`CopyToOutputDirectory=PreserveNewest`) and loaded from `AppContext.BaseDirectory`. A unit test asserts the hash, so an accidental file swap fails CI.

*Why a loose file over an embedded resource:* `InferenceSession` takes a path or bytes, and both work. A loose file is simpler to inspect and keeps the managed assembly small.

### D3: `IVoiceActivityDetector` and `AudioTrimmer`

```csharp
internal interface IVoiceActivityDetector { IReadOnlyList<SpeechSegment> DetectSpeech(ReadOnlySpan<float> samples, CancellationToken cancellationToken); }
internal static class AudioTrimmer
{
    // null when segments is empty; otherwise [first.Start - pad, last.End + pad] clamped to bounds
    public static float[]? Trim(float[] samples, IReadOnlyList<SpeechSegment> segments, int padSamples = 4800);
}
```

`SileroVoiceActivityDetector` owns one `InferenceSession` (CPU EP, `IntraOpNumThreads = 1`, `InterOpNumThreads = 1`, `GraphOptimizationLevel = ORT_ENABLE_ALL`). Calls are serialized with a `lock`, because the model state is per call anyway and dictations do not overlap.

The detector creates the session on first use through a `Lazy<T>` in `ExecutionAndPublication` mode. `VoiceActivityWarmupService : IHostedService` starts that load in the background from `StartAsync`, followed by one 512-sample inference. It does not await it, so startup is not delayed. A `DetectSpeech` call that arrives before the load has finished waits for it. `Lazy<T>` caches a load failure, so every later call throws the same error and dictation uses the D4 fallback. The warm-up logs the failure once as a warning and does not fail startup. `services.AddVoiceActivity()` is registered in `AppHost.Create` before `AddDictation()`, so the dictation controller still starts last.

### D4: Integration into dictation

`StartRecordingAsync` adds `settings.VoiceActivity.Enabled` to `ActiveDictation` as `VoiceActivityEnabled`, next to the task, languages and text insertion settings taken at the press. In `DictationController.ProcessAsync`, before `TranscribeAsync`:

```
if dictation.VoiceActivityEnabled:
    try   segments = detector.DetectSpeech(clip.Samples, _stopping.Token)
          trimmed  = AudioTrimmer.Trim(clip.Samples, segments)
          if trimmed is null → ShowNoSpeech; return
          samples = trimmed
    catch (OperationCanceledException) when stopping → rethrow
    catch (Exception ex) → log warning (no audio), samples = clip.Samples
```

The log records the original and trimmed durations and the VAD time in milliseconds.

The detector checks the token before each 512-sample window, so `DictationController.StopAsync` does not wait for a whole scan. A 400 s recording takes about 2.5 s to scan, which would use most of the 4 s shutdown timeout. A cancellation during shutdown is rethrown rather than handled as a failure, so the app does not go on to transcribe the untrimmed audio while it stops.

Settings: `VoiceActivitySettings(bool Enabled = true)`, added to `AppSettings` as `public VoiceActivitySettings VoiceActivity { get; init; } = new();`. `JsonSettingsStore.Load` replaces a `null` section with its default, as the `AppSettings` rules require.

Settings window:
- `DictationSectionViewModel` gets an observable `TrimSilence` property, initialized from `settings.VoiceActivity.Enabled`. Its `Rebase` adds `TrimSilence = DraftValue.Rebase(TrimSilence, previous.VoiceActivity.Enabled, current.VoiceActivity.Enabled)`.
- `SettingsViewModel.BuildSettings` adds `VoiceActivity = new VoiceActivitySettings(Dictation.TrimSilence)`. `HasChanges`, **Save** and the rebase on external saves then work without further changes.
- `SettingsDialog.xaml` gets a "Trim silence before transcription" checkbox in the Dictation section, bound to `Dictation.TrimSilence`.

## Risks / Trade-offs

- [Quiet or distant speech falls below threshold 0.5 and gets reported as "no speech", losing a dictation.] → The user can disable VAD in settings. The hardware test with the real German clip, including a quiet variant at −20 dB, checks detection. The threshold is a single constant if tuning is needed.
- [ONNX Runtime adds about 12 MB of native binaries, and a second native runtime next to ggml.] → An accepted cost. The CPU EP only, with no DirectML package.
- [Per-window cost could exceed the 300 ms per 30 s bound. A 30 s clip is about 937 windows of 512 samples, which leaves about 0.32 ms per window including the ONNX Runtime call overhead. Upstream only promises "under 1 ms per chunk" on one CPU thread.] → Pre-allocated `OrtValue` buffers (D1). The explicit benchmark test measures it on the target laptop, and it is a real test, not a formality. If it fails, there is a fallback. Trimming needs only the first and the last speech, so the detector can scan forward until the first speech, then scan the tail in blocks from the end. It runs each block forward with a fresh state and a short lead-in, and stops at the first block with speech. A typical dictation then needs a few seconds of windows instead of the whole clip. A silent clip still needs a full scan.
- [The 250 ms minimum speech length could drop a one-word dictation such as "Ja" or "OK". Missing speech costs more than wrongly finding it: missed speech loses the dictation and the user has to repeat it, while a false detection only lets Canary transcribe a little noise.] → The manual end-to-end check includes a one-word dictation. If it is dropped, lower the minimum speech length, which is one constant, and repeat the silence and fan-noise checks.
- [This machine has a second `onnxruntime.dll` in `C:\Windows\System32` (Windows ML, version 1.17). The app copies its own DLL next to the executable because it sets `RuntimeIdentifier` `win-x64`. The test project sets no runtime identifier, so there the DLL is resolved through `runtimes\win-x64\native` and `deps.json`. A wrong resolution would load an incompatible runtime and fail in confusing ways.] → The warm-up logs the loaded version (`OrtEnv.Instance().GetVersionString()`), and a unit test asserts it is 1.30.0, so both the app and the test host would show a wrong load.
- [Trimming with 300 ms padding might clip soft word endings ("…ung").] → The padding is generous compared with the Silero default of 30 ms, and it is one constant.
