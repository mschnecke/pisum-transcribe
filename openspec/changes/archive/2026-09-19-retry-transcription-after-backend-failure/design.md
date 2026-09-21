## Context

`TranscribeCppTranscriber` runs every native call on one dedicated worker that reads a FIFO channel (`add-transcription-engine` D3). When a run fails with a backend error, `Run` completes the request with `TranscriptionFailedException` and then calls `RecoverFromBackendFailure`. With `auto` on Vulkan, that method sets `Loading`, disposes the engine and calls `Load(model, Cpu, generation)` synchronously, before the worker reads the next item. `Load` publishes `Ready` only if no newer load was requested (`TryPublishReady`); otherwise it releases the engine unused.

Two facts shape the shutdown path:
- `TranscribeAsync` awaits the request's `TaskCompletionSource` without the caller's token. The caller's token only takes effect through the native run, which links it with the worker's `_stopping` token.
- Hosted services stop in reverse registration order, one after another. `AddDictation` is registered after `AddTranscription`, so `DictationController.StopAsync` runs first: it cancels its token and awaits its processing task without a bound, before `TranscribeCppTranscriber.StopAsync` cancels `_stopping`.

The CPU reload observes neither token of the caller: `Model.Load` takes no token, and the warm-up uses only `_stopping`.

In transcribe.cpp v0.2.3, the pinned native version, a run needs more memory the longer its clip is:
- Canary runs its encoder over the whole clip in one graph, which it builds for the clip's length (`src/arch/canary/model.cpp`). Only Whisper splits long audio into chunks.
- The relative-position attention computes a (2T−1) × T score matrix per head, also on the flash-attention path (`src/conformer/conformer.cpp`). T is 12.5 encoder frames per second of audio. With 8 heads in f32, the matrix takes about 1 MB at the 10 s warm-up, 144 MB at 120 s, and 1.6 GB as a single tensor at the 400 s limit.
- The scheduler's compute buffers grow to fit the largest graph so far (`ggml/src/ggml-alloc.c`). The first long dictation after the warm-up is therefore the first run that needs that memory.

If the allocation fails, the run returns `ErrOom`. A lost Vulkan device surfaces as `ErrBackend`, through the exception guard around `transcribe_run` (`src/transcribe.cpp`). `Classify` counts both as backend errors, so long dictations are the ones most likely to take the retry path.

## Goals / Non-Goals

**Goals:**
- Run a request that failed with a backend error on Vulkan once more on the CPU, inside the worker, with the existing serialization, status and fallback rules.
- Complete the request on every outcome of the reload, so no caller waits forever.

**Non-Goals:**
- No change to how the reload itself works: same load, warm-up, status sequence, log lines and load failure handling.
- No change to what counts as a backend error (`Classify`).
- No cancelling of a running transcription by the user.

## Decisions

### D1: Run the request again inline, right after the reload

`Run` no longer completes the request before recovering. On a backend error with `auto` on Vulkan, it keeps the `RunWorkItem`, calls the recovery, and if the engine is now `Ready` on the CPU, runs the same item again on the worker thread. The retry uses the same samples and options that the caller validated, and the same caller token.

Running inline puts the retry ahead of every queued item without touching the queue, and it keeps "one native call at a time" unchanged.

*Alternatives:* putting the item back into the channel. A `Channel<T>` can only append, so the retry would run behind the requests queued after the failed one, against the issue's ordering. A second, priority queue for retries was rejected as more machinery for one item.

### D2: One retry follows from the backend, not from a counter

The reload always loads with `BackendPreference.Cpu`, so the retry runs on the CPU. A backend error on the CPU already goes to the "release and `Failed`" branch of `RecoverFromBackendFailure`, which does not reload. A second retry is therefore impossible, and the work item needs no retry flag. Unit tests assert the native call sequence, so a later change that breaks this shows up as an extra `Run`.

### D3: Each outcome of the reload maps to one outcome of the request

`RecoverFromBackendFailure` reports whether the engine is `Ready` on the CPU for this generation. `Run` completes the request from that result. The retry must not go through `Run`'s "no engine" check, which would report `TranscriberNotReadyException` ("still loading") instead of a failure.

| Reload outcome | Status | Request |
|---|---|---|
| `Ready` on the CPU | `Loading`, then `Ready` (CPU) | run again; its result, or its failure under the existing rules for the CPU backend |
| load or warm-up failed | `Failed`, with the load failure message, as today | `TranscriptionFailedException` with the original Vulkan status |
| replaced by a newer load (D5) | decided by the newer load | `TranscriptionFailedException` with the original Vulkan status |
| application stopping (warm-up cancelled) | `NotLoaded`, as today | cancelled |
| reload succeeded, but `_stopping` or the caller token is cancelled before the retry | `Ready` (CPU) | cancelled, not run again |

Logging, without text or audio:
- The failure log line of the Vulkan run adds the audio duration. That lets field logs confirm that out-of-memory errors depend on clip length (see Context).
- A warning says that the request runs again on the CPU backend.
- The existing "Ran … on CPU" line logs the retry. `TranscriptionResult.ProcessingTime` holds only the CPU run, so the time stays tied to the backend in that line. The load and warm-up durations of the reload are already logged by `LoadAndWarmUp`.

### D4: A caller's cancellation releases it at once

`TranscribeAsync` awaits `item.Completion.Task.WaitAsync(cancellationToken)`, as `LoadAsync` already does. On exit, `DictationController` cancels its token and is released at once, even while the worker is still reloading on the CPU. When the worker reaches the item, it skips the retry, because the token is cancelled (D3).

Without this change, the dictation's stop would wait for the whole CPU load and warm-up. That time comes out of the host's 4 s shutdown timeout before the transcriber is even asked to stop, so the watchdog would end the process at 4.5 s with the model unreleased.

The change applies to every request, not only the retry. The only caller, `DictationController`, cancels its token only on exit. A cancelled request that is still queued is skipped by the existing check at the start of `Run`. A running request is aborted through the linked token, as today. The item keeps its samples in memory until the worker drops it.

*Alternatives:*
- Observing the caller's token in the reload's warm-up. Rejected: one caller's cancellation would abort the engine's recovery for everyone, and `Model.Load` cannot be cancelled anyway.
- Cancelling the pending item from `TranscribeCppTranscriber.StopAsync`. Rejected: the transcriber stops after the dictation controller, which is too late.

### D5: A newer load during the recovery rejects the request

A newer load can replace the recovery at two points:
- It was requested while the Vulkan run was going: `TrySetStatus(generation, Loading)` fails, and the engine is released as today.
- It was requested during the CPU load: `TryPublishReady` fails, and `Load` releases the CPU engine unused.

In both cases the request is rejected with `TranscriptionFailedException`, and the newer load decides the status.

*Alternative:* run the request on the replaced CPU engine before releasing it. This matches the reload rule that requests submitted before a reload finish on the previous model. It was rejected because `Load` would have to keep a replaced engine alive for one run, and the newer load would start later. The case needs a settings change during a dictation that failed on the GPU.

## Risks / Trade-offs

- [The CPU is slow with the 1B model. It needed 15.3 s for 10.7 s of audio (`add-transcription-engine` notes), so a 400 s dictation can take several minutes after the reload. It may take longer, because the attention cost grows with the square of the length (see Context). During that time the overlay shows "Transcribing…", the hotkey answers "Still processing…", and a transcription can't be cancelled.] → Accepted: re-dictating is worse than waiting. **Exit** still ends the process within 5 s. The native run checks for an abort before the encoder and between decode steps. The encoder pass itself can't be interrupted, so during it `StopAsync` stops waiting after 3 s without releasing the model (`add-transcription-engine` D8). Cancelling a running transcription is the follow-up #11.
- [An out-of-memory error on Vulkan moves the session to the CPU until restart. These errors depend on clip length (see Context), so one long dictation slows every later one.] → Existing behavior, out of scope for this change; getting back to the GPU is the follow-up #12. The audio duration in the failure log line (D3) makes it visible in the logs.
- [The request's `TaskCompletionSource` stays open across the reload, so an outcome that never completes it would hang the caller.] → D3 maps every outcome to a completion, unit tests cover each row, and D4 bounds the caller's wait by its own token.
- [When the CPU reload fails, the user gets two notifications: the engine's load failure and the dictation's "Transcription failed."] → The same as today, only in the other order.
