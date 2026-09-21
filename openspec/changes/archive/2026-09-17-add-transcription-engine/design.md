## Context

This change builds on `scaffold-app-shell` (host, settings, tray `SetStatus` / `ShowNotification`, to which this change adds `SetToolTip`) and `add-model-management` (`ModelCatalog`, `IModelStore.IsInstalled`, `GetModelPath`, the `ModelInstalled` event, and `SpeechModel.Languages` and `Sha256`).

`TranscribeCppSharp` 0.2.0 (NuGet, MIT) wraps transcribe.cpp v0.2.3 and provides:
- `Backends.InitDefault()` / `BackendAvailable(BackendRequest)`
- `Model.Load(path, p => p.WithBackend(...))`
- `model.CreateSession()` and `session.GetLimits().EffectiveMaxAudioMs`
- `session.Run(ReadOnlySpan<float>, Action<RunParamsBuilder>, CancellationToken)`, with `WithTask`, `WithLanguage` and `WithTargetLanguage`
- `TranscribeException.StatusCode`

The native API is blocking, and it is not thread-safe: at most one compute call may run per model.

Checked against the 0.2.0 binary:
- `session.Run` throws `TranscribeException` for every status other than `Ok`, including `ErrOutputTruncated`. After that exception, `session.FullText` still holds the partial text. `ErrAborted` becomes `OperationCanceledException`.
- `session.Run` passes the raw native handle, not the `SafeHandle`. Disposing the session or model on another thread while a run is in progress frees memory the native call still uses.

## Goals / Non-Goals

**Goals:**
- A warm, serialized engine with Vulkan→CPU fallback behind `ITranscriber`.
- Hermetic unit tests for fallback, serialization, validation and failure handling, with no native DLLs or models involved.
- Explicit hardware tests that answer the Phase 0 question from `docs/idea.md`: is warm latency for a 10 s German clip well under 3 s on the Xe iGPU, and is Vulkan faster than CPU?

**Non-Goals:**
- No sherpa-onnx or sidecar implementation. The abstraction alone is the insurance `docs/idea.md` asks for, and an unused second engine would be speculative.
- No streaming or partial results. Canary has no streaming mode.
- No custom vocabulary or prompt biasing (unconfirmed in transcribe.cpp).
- No CUDA backend. The NuGet runtime ships only CPU and Vulkan.
- No GPU device picker. Vulkan auto-selects the device.

## Decisions

### D1: `ITranscriber` as the consumer seam

```csharp
internal interface ITranscriber
{
    TranscriberStatus Status { get; }            // NotLoaded | Loading | Ready | Failed
    string? ActiveBackend { get; }               // "Vulkan" | "CPU" when Ready
    TimeSpan MaxInputDuration { get; }
    event EventHandler<TranscriberStatus>? StatusChanged;
    Task LoadAsync(SpeechModel model, BackendPreference backend, CancellationToken ct);   // NotLoaded or Failed
    Task<TranscriptionResult> TranscribeAsync(float[] samples, TranscriptionOptions options, CancellationToken ct);
}
sealed record TranscriptionOptions(TranscriptionTask Task, string SourceLanguage, string TargetLanguage);
sealed record TranscriptionResult(string Text, TimeSpan AudioDuration, TimeSpan ProcessingTime);
```

Validation (`TranscriptionOptionsValidator`) is a pure function over `(SpeechModel, TranscriptionOptions)`, so it is unit-testable and reused by `add-settings-window` to restrict the dropdowns.

`LoadAsync` is valid in `NotLoaded` and `Failed`, so a model downloaded again after a damaged-file failure (D7) loads without a restart. In other states it throws `InvalidOperationException`. `add-settings-window` extends it to `Ready`.

Failed runs throw `TranscriptionFailedException`, which carries the native status code (D10). It joins `LanguageNotSupportedException`, `AudioTooLongException` and `TranscriberNotReadyException`.

### D2: Thin native seam for testability

`TranscribeCppTranscriber` contains the logic: state machine, fallback, queue, limits and damaged-file handling. It talks to `INativeSpeechEngineFactory` → `INativeSpeechEngine` (`Run`, `MaxAudio`, `Dispose`). The production implementation of that seam is a roughly 50-line adapter over `Model` and `Session`, and it is the only code that references TranscribeCppSharp. Unit tests fake the seam to simulate a Vulkan load failure, a warm-up failure, `ErrGguf`, a backend error or truncated output during a run, or a slow run.

For the `transcribe` task, the adapter passes the source language as the target language. Canary v2 models (canary-1b-v2, canary-180m-flash) infer translation from source ≠ target, so passing the `targetLanguage` setting would translate anyway.

*Alternative:* using TranscribeCppSharp types directly. This was rejected because its classes are sealed and need real native binaries and models, which would make every fallback test a hardware test.

### D3: One dedicated worker with a FIFO channel

All native calls (load, warm-up, run, dispose) go through an unbounded `Channel<WorkItem>` read by a single `TaskCreationOptions.LongRunning` worker. The consequences:
- The concurrency rule ("one compute per model") holds for load, warm-up and run alike.
- Requests are handled in strict FIFO order. `SemaphoreSlim` does not guarantee FIFO.
- A multi-second model load does not tie up a thread-pool thread.

`TranscribeAsync` checks `Status == Ready`, validates options and length, and then enqueues a `TaskCompletionSource`. Validation failures never reach the queue.

### D4: Backend selection

`Backends.InitDefault()` runs once on the worker. The selection goes as follows:
- **Auto:** if `BackendAvailable(Vulkan)`, load with `BackendRequest.BackendVulkan` and run warm-up. A backend failure (see the table) from the Vulkan backend disposes the session and model, logs the reason, and repeats the load and warm-up with `BackendCpu`.
- **Vulkan:** the same load, with no fallback: `Failed`.
- **CPU:** CPU only.

A backend failure during warm-up counts like one during load, because the known Intel Xe failure (`vkCreateDevice` → `ERROR_DEVICE_LOST`) shows up at first compute, not at load.

Errors are classified by status code, during load and warm-up as well as during runs (D10):

| Error | Class | During load or warm-up | During a run |
|---|---|---|---|
| `ErrBackend`, `ErrOom`, `DllNotFoundException` | backend failure | Auto on Vulkan: retry on CPU. Otherwise: `Failed` | D10 |
| `ErrGguf`, `ErrUnsupportedArch`, `ErrUnsupportedVariant` | invalid model | D7 without a CPU retry, then `Failed` | – |
| `ErrOutputTruncated` | truncated | warm-up counts as successful | partial text returned (D10) |
| `ErrAborted` (`OperationCanceledException`) | cancelled | shutdown (D8) | shutdown (D8) |
| any other status | other | `Failed` | request fails, status stays `Ready` |

### D5: Warm-up

After the load, the worker runs one inference, configured as transcribe with language "en", and discards the result. The status becomes `Ready` only after warm-up succeeds. `ErrOutputTruncated` counts as success (D4). The input depends on the backend:
- **Vulkan:** 10 s of fixed pseudo-random noise, uniform in [-0.1, 0.1] with a constant seed. ggml-vulkan compiles GPU pipelines for the tensor sizes it meets, so a short input leaves the pipelines of a normal dictation to the first real request.
- **CPU:** 1 s of silence. The CPU backend compiles no pipelines, and 10 s of input would add about 10 s to every start with the 1B model.

Measured on the target laptop with `canary-1b-v2-q8_0` on Vulkan and a 10.7 s clip (`notes.md`):
- **Cold driver shader cache** (the first start of an executable): after 1 s of silence, the first real run took 3.6–5.8 s, against 1.2–2.3 s for later runs. After 10 s of noise, it took 1.6–2.5 s, in line with later runs. The warm-up itself took 15–21 s either way.
- **Warm cache:** the noise warm-up takes about 1.3 s instead of about 0.8 s (0.5 s instead of 0.2 s with the 180M model).

Why noise, and not silence or speech: 10 s of silence makes the 1B model produce about 165 characters, a decode of about 5 s on every start. Noise produced no text on either Canary model. A speech clip was not faster than noise and would be an extra asset to ship.

The configuration is fixed, so `LoadAsync` needs no language settings. Task and languages only change Canary's decoder prompt tokens, so the fixed warm-up compiles the same GPU pipelines as a real run of similar length.

Not covered: with a warm cache, the first real run stays about 0.4 s slower than later runs with every warm-up input tried, including the speech clip itself. That is within run-to-run variation (1.2–3.6 s), not pipeline compilation.

### D6: Limits

`MaxInputDuration = TimeSpan.FromMilliseconds(session.GetLimits().EffectiveMaxAudioMs)`. If the native value is ≤ 0, it falls back to 400 s. The recorder in `add-dictation-workflow` uses this value as its automatic-stop limit.

### D7: Damaged model handling

When `Model.Load` throws with an invalid-model status (D4), the worker hashes the file (`SHA256.HashDataAsync` over a `FileStream`) and compares it with `SpeechModel.Sha256`. On a mismatch, it deletes the file, so `IModelStore.IsInstalled` becomes false and the setup tray item from `add-model-management` reappears, and it notifies "Model file was damaged and has been removed. Download it again." On a match, it keeps the file and notifies "This model can't be loaded by this version of Pisum Transcribe." Hashing only on failure keeps startup fast. With Auto on Vulkan, there is no CPU retry first, because the same file would fail again.

### D8: Hosting and status

`TranscriberHostedService` loads the selected model at start if installed. It subscribes to `ModelInstalled` and loads when the installed model is the selected one. It maps `StatusChanged` to tooltips with the new `ITrayIconService.SetToolTip(string)`, which keeps the current icon, and `Failed` to `ShowNotification`. Settings section: `TranscriptionSettings(BackendPreference Backend = Auto, TranscriptionTask Task = Translate, string SourceLanguage = "de", string TargetLanguage = "en")`.

On shutdown, `StopAsync` cancels the in-flight run's token, which TranscribeCppSharp maps to the native abort, and waits up to 3 s for the worker. Only the worker disposes the session and then the model, after its current native call has returned. If the worker is still busy after 3 s, `StopAsync` returns without disposing: disposing from another thread would free memory the running native call still uses (see Context), and the process ends anyway. `Model.Load` takes no cancellation token, so an exit during loading is bounded only by this wait.

### D9: Hardware tests

These are Category `Hardware`, `[Fact(Explicit = true)]`:
- A model path comes from the installed catalog model. If it is missing, the test calls `Assert.Skip`.
- German audio comes from the environment variable `PISUM_TRANSCRIBE_TEST_AUDIO`: a WAV file of about 10 s of German speech, read with `PcmExtensions.ReadWavToPcm`. If it is missing, the German tests skip.
- English audio comes from the environment variable `PISUM_TRANSCRIBE_TEST_AUDIO_EN`: a WAV file of about 10 s of English speech, read the same way. If it is missing, the English test skips. The German tests do not depend on it.
- **Integration (German clip):** de→en translate returns non-empty English text. de transcribe, with the target setting still `en`, returns non-empty text that differs from the de→en translation, which shows the adapter did not translate.
- **Integration (English clip):** en→de translate (verifies how `WithTask` / `WithTargetLanguage` behave for Canary) returns non-empty text that differs from en transcribe of the same clip, which shows the model translated.
- **Benchmark:** for each installed catalog model × {Vulkan, CPU}, measure load time, warm-up time with the D5 input for the backend, and the median of 5 warm runs, and write a table to `TestContext.Current.SendDiagnosticMessage`. It asserts nothing about speed. The numbers inform the default model and backend.

### D10: Errors during a run

The worker handles a failed run by its class (D4):
- **Backend failure:** the request fails with `TranscriptionFailedException`. With Auto on Vulkan, the worker then reloads on CPU before it reads the next work item: status `Loading`, dispose session and model, load on CPU and warm up, then `Ready` with backend `CPU`, or `Failed`. Requests queued before the failure run afterwards on CPU. New requests during the reload are rejected as loading. CPU stays in use until the application restarts or the model or backend setting changes. The reason is logged. There is no extra notification, because the failed dictation already notifies the user. With `vulkan`, or when the failure happens on CPU, the worker disposes session and model and sets `Failed`.
- **Truncated:** the adapter catches `ErrOutputTruncated`, reads `session.FullText` and returns it with a truncated flag. The transcriber logs the flag, without the text, and returns the text as a normal result.
- **Other:** the request fails with `TranscriptionFailedException`, and the status stays `Ready`.

The reload follows the same steps as the engine reload in `add-settings-window` D3, so that change can reuse it.

The user-facing message of `TranscriptionFailedException` is "Transcription failed." `add-dictation-workflow` shows it in its error notification. The native status code goes to the log, not into the message.

## Risks / Trade-offs

- [transcribe.cpp 0.x ABI churn; the wrapper depends on the exact native version.] → Pin `TranscribeCppSharp` and `.Native.win-x64` exactly in `Directory.Packages.props`. `INativeSpeechEngine` confines the churn to one adapter.
- [A native crash (access violation) in ggml/Vulkan kills the process, and no managed fallback can catch it.] → The log records "loading on Vulkan" before the attempt. If the previous run's log ends in a Vulkan load, the user can set backend `cpu`, which `add-settings-window` exposes. Automatic crash-loop detection is deferred until it is observed.
- [`TranscriptionTask.Translate` is documented in TranscribeCppSharp as "translate to English"; en→X translation might need a different parameter combination for Canary.] → transcribe.cpp's Canary validation for v0.2.3 (`docs/porting/families/canary.md`) reports EN→de translation as passing on all four variants. The explicit en→de integration test in D9 confirms it through this wrapper. If it fails, the validator gets restricted in a follow-up.
- [A GPU error after `Ready` loses the dictation that hit it.] → D10 reloads on CPU, so the next dictation works. The failed audio is not retried.
- [Memory: Q8_0 keeps about 1.1 GB or more resident for the app's lifetime.] → This is an accepted trade-off for latency. The 180M model is available for low-memory machines.
- [The warm-up input could hit a hallucination or long-decode path. 10 s of silence does on the 1B model.] → Vulkan warms up on noise, which produced no text on both catalog models, and CPU on 1 s of silence. The output is discarded, and `ErrOutputTruncated` counts as success (D4). A future model that produces text on the noise makes the warm-up slower, not wrong.
- [The driver's shader cache was per executable in the measurements, so the first start of a new executable spends 15–20 s in `Loading` on Vulkan.] → Accepted. The tooltip shows "Loading model…", and later starts warm up in about 1.3 s.
