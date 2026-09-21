## Context

`TranscribeCppTranscriber` runs every native call on one dedicated worker that reads a FIFO channel. When a run fails on Vulkan with a backend error and the backend is `auto`, `Run` calls `RecoverFromBackendFailure`. That method sets `Loading`, disposes the engine and loads the model on the CPU for the same load generation. `Run` then runs the failed request again on the CPU (`retry-transcription-after-backend-failure` D1–D3). `Classify` counts `ErrOom` and `ErrBackend` as the same `FailureClass.Backend`, both in `LoadEngine` and in `Run`. Nothing ever moves the engine back to Vulkan, because only `LoadAsync` (startup, model installed, settings change) queues a load.

`LoadAsync` takes the next load generation, sets `Loading` without clearing `_model` and `_activeBackend`, and queues a `LoadWorkItem`. Requests already queued therefore run on the loaded engine first, and new requests are rejected as still loading. The worker skips a load that a newer one replaced, and releases the old engine before it loads the new one. `TranscribeAsync` validates a request against `_model` and `_maxInputDuration` before queuing it. `Run` rejects a request with `TranscriberNotReadyException` when it finds no engine. `DictationFeedback` and `ModelSectionViewModel` redraw on every `StatusChanged` and read `ActiveBackend` each time, so another `Ready` with a new backend updates the tray and the settings window.

Once a dictation's transcription is cancelled (#11), the dictation has ended. The next hotkey press starts a recording whenever the status is `Ready`, even while the engine is still finishing the cancelled run on the CPU. That new dictation is transcribed once the engine has stopped the cancelled run (dictation scenario "Next dictation while the engine finishes the cancelled one").

Task 1 measured long clips on Vulkan on the target laptop (`notes.md`). With the default model, clips of 30, 60, 120, 240 and 399 s all complete on the Arc 140T, in 1.9, 4.1, 6.7, 41.8 and 173.5 s. So no clip runs out of GPU memory there. The engine reports a maximum input of 400 s on both backends. An out-of-memory error appears only with a forced GPU buffer limit, which needs `GGML_VK_FORCE_MAX_BUFFER_SIZE` together with `GGML_VK_FORCE_MAX_ALLOCATION_SIZE`. `GGML_VK_FORCE_MAX_ALLOCATION_SIZE` alone changes nothing. With both at 64 MiB, the 399 s clip fails with `ErrOom`, and with both at 32 MiB, the 240 s clip does. In both cases, the allocation that fails is the cross-attention KV cache, not the encoder's attention score matrix that the #9 estimate expected. That cache takes 16 KiB per encoder frame of 80 ms, and transcribe.cpp allocates it after the encoder has run. A failed run therefore returns only after its whole encoder pass: 234 s for 399 s of audio and 117 s for 240 s, under caps that also slow the encoder down. After the failure, the same engine still runs the warm-up input, and loading the model on Vulkan again works. `cancel-running-transcription` measured long clips on the CPU: one encoder pass takes 19.3 s for 60 s of audio, 43.5 s for 120 s and 214.1 s for 399 s.

## Goals / Non-Goals

**Goals:**
- Confirm on the target laptop that long clips fail on Vulkan with `ErrOom`, before the engine starts reacting to it.
- Return to Vulkan without rejecting any request, so a dictation that starts after a cancel isn't lost.
- Limit the cost when memory runs short for a reason other than the clip length to one extra round of reloads.

**Non-Goals:**
- No change to the reload on the CPU or the retry there (#9), or to cancelling (#11).
- No change to what happens after a load or warm-up fails, including with `ErrOom`.
- No attempt to predict whether a clip fits into GPU memory before running it.

## Decisions

### D1: Measure first, then decide at a checkpoint

Task 1 adds a `Hardware` test to `TranscribeCppBenchmarkTests`, on the default model and the German clip from `HardwareTestAssets`, like the tests from #11:

1. Load on Vulkan, warm up, and record the engine's `MaxAudio`.
2. Run 30, 60, 120, 240 and 399 s clips, built by repeating the German clip, in that order. The compute buffers only grow, so increasing lengths match a real session. For each clip, record the outcome (completed, or the native status), the time until `Run` returned, and the processing time.
3. After the first failure, run the 10 s warm-up input on the same engine, to see whether the engine still works.
4. Release the engine, load it on Vulkan again, and run the 10 s input. This is the step the return depends on.
5. Release the engine, load it on the CPU, and record its `MaxAudio`.
6. Send each row to the diagnostic messages as soon as it is measured, so the rows so far survive a native crash.

The test asserts nothing about the outcome. The results go into `notes.md`.

**Forced allocation limit.** The bundled `ggml-vulkan.dll` reads a set of `GGML_VK_*` environment variables when the backends initialize, so they have to be set before the test process or the app starts. `GGML_VK_FORCE_MAX_BUFFER_SIZE` caps the size of a single GPU buffer in bytes, and a larger buffer fails with `ErrOom`. `GGML_VK_FORCE_MAX_ALLOCATION_SIZE` is set to the same value, so the weights are split into buffers below the cap. On its own, it changes nothing (`notes.md`). If every clip completes, the test runs again with a cap that makes the long clips fail while the weights and the 10 s warm-up still fit. The buffer that fails is the cross-attention KV cache, which takes 16 KiB per encoder frame of 80 ms. So 32 MiB makes clips above about 164 s fail, and 64 MiB clips above about 328 s. The measured values are in `notes.md`, and the hand check uses 32 MiB. The cap simulates a single buffer that doesn't fit. It doesn't simulate a GPU that is completely full, but both surface as the same status code, which is all the transcriber sees.

**Checkpoint:** the result of task 1 decides how the work continues:

| Result | Decision |
|---|---|
| A clip fails with `ErrOom`, step 4 succeeds, `MaxAudio` is the same on both backends | Continue. |
| Every clip completes up to 399 s | Run the test again with the forced allocation limit. If a clip then fails with `ErrOom` and step 4 succeeds, continue. The change then serves GPUs with less memory, and hand check 3.2 uses the same limit. Otherwise stop, as below. |
| `MaxAudio` differs between the backends | Continue with the smaller limit during the return (D4). |
| A clip fails with `ErrBackend`, for example a Windows TDR reset during a long GPU submission | Stop and revise the change with `/opsx:update`. The reset becomes its own issue, with `GGML_VK_MAX_NODES_PER_SUBMIT` as a lead. |
| The process crashes on a long clip | Stop. Report a bug that long clips crash the app on Vulkan. |
| The reload on Vulkan in step 4 fails | Stop. The return can't work on this hardware. |

*Alternative:* a spike before the proposal. It was rejected because the test has to be written anyway and stays useful as a benchmark. The proposal is cheap to revise.

### D2: Only an `ErrOom` from a run leads to a return

`Classify` stays as it is, so the load path and the reload on the CPU don't change. `Run` keeps the `NativeEngineException` of the Vulkan run and checks its status: only `ErrOom` from a run on Vulkan with `_backendPreference == Auto` can lead to a return. An `ErrOom` during load or warm-up still falls back to the CPU in `LoadEngine` and stays there. If the 10 s warm-up doesn't fit, a return would fail the same way every time.

*Alternatives:*
- A separate `FailureClass.OutOfMemory`. Rejected: every caller of `Classify` would then have to treat it like `Backend` again, for one check in `Run`.
- Reacting to any backend error on a clip that is long enough. Rejected by requirement 3 of the issue (a lost device stays on the CPU). D1 reopens this if long clips fail with `ErrBackend`.

### D3: The two checks are worker state, reset by the loads that `LoadAsync` queues

The worker keeps two fields, which only it touches, like `_engine`:
- `_longestVulkanRun`: the longest audio of a request that completed on Vulkan. It starts at the length of the Vulkan warm-up input (10 s) and grows after each request that returns text on Vulkan, including truncated output. Cancelled and failed runs don't count, because it's unknown how far they got.
- `_returnedWithoutVulkanRun`: set when a return is queued, cleared when a request completes on Vulkan.

A return is queued only if the failed request's audio is longer than `_longestVulkanRun` (check 1) and `_returnedWithoutVulkanRun` is false (check 2).

Only requests count, not warm-ups. The first warm-up is already the 10 s starting value of check 1. If the return's own warm-up cleared check 2, a 15 s clip that runs out of memory because GPU memory stays short would lead to a return every time: check 1 would pass (15 s is longer than 10 s), and check 2 would never hold. That is the loop check 2 prevents.

The two fields reset when the worker processes a load that `LoadAsync` queued, and not for a return. `LoadWorkItem` gets a flag that marks a return. If a return reset the fields, `_longestVulkanRun` would drop back to 10 s after every return, and check 1 would rarely hold the engine on the CPU. The fields are never saved, because GPU memory conditions differ between sessions.

*Alternative:* remembering the failing length instead (option B of the issue). Deferred: its only benefit is skipping the Vulkan attempt that fails anyway, and D1 measures how long that attempt takes.

### D4: The return keeps the status `Ready`, and requests wait for it

When `Run` has finished with the failed request (it ran on the CPU, failed there with an error that isn't a backend error, or was cancelled by its caller), it queues the return if all of these hold:
- the Vulkan error was `ErrOom` with backend `auto` (D2)
- the engine is `Ready` on the CPU for the generation of the failed run
- both checks pass (D3)
- the application isn't stopping

The return is a `LoadWorkItem(model, Auto, generation, return: true)` with the current load generation. It doesn't take a new generation and doesn't change the status. When the worker reaches it:
- If a model or backend change replaced it, the worker skips it, as it skips any replaced load. The CPU engine is still loaded, so the requests before the newer load run on it, as the rules for a reload after a settings change require.
- Otherwise the worker disposes the CPU engine without clearing `_model`, `_activeBackend` and `_maxInputDuration`, and calls `Load(model, Auto, generation)`. This is the same load, fallback and warm-up as a first load. `TryPublishReady` then publishes `Ready` with the new backend, which updates the tray.

The consequences:
- Requests queued behind the failed one run on the CPU before the return, in channel order.
- A request submitted during the return passes validation against the unchanged model and queues behind the return. It then runs on Vulkan, or on the CPU if Vulkan failed and `LoadEngine` fell back.
- A hotkey press during the return starts a recording, because the status is `Ready`.
- If a model or backend change replaces the return while it loads, `TryPublishReady` fails and `Load` releases the new engine. The requests waiting for the return then find no engine, and `Run` rejects them as still loading.
- If loading fails on both backends, `Load` sets `Failed`, and `Run` rejects the waiting requests with that status.
- The CPU engine is disposed before the Vulkan load, so two models are never loaded at once.

This works because the return keeps the same model: the languages and the maximum input that requests were validated against don't change. D1 checks that `MaxAudio` is the same on both backends. If it differs, the return sets `_maxInputDuration` to the smaller of the CPU limit and the limit of the Vulkan engine that failed, until the return publishes its engine. A request accepted during the return then fits on both backends. A reload after a settings change has to reject requests, because it may change the model.

*Alternatives:*
- Queuing the return like a settings change, with a new generation and `Loading` (this design's first version). Rejected: after a cancel the dictation has ended. A recording that starts before the aborted CPU run returns (possibly minutes) ends while the status is `Loading`. Its transcription is then rejected and its audio lost, against the #11 scenario "Next dictation while the engine finishes the cancelled one".
- No return after a cancel. Rejected: cancelling a CPU retry that takes minutes is exactly the case #11 was built for. The session would lose the GPU there, and a quick retry of a 20 s dictation would take about 43–54 s instead of 29–34 s.
- Returning when the next request arrives. Rejected: that request would always wait 3–5 s for the reload instead of the reload using idle time, and the return would need its own inline load path.
- Reloading on Vulkan inline, right after the failed request, before the requests queued behind it. Requests queue behind the failed one only after a cancel (#11), when the user dictates again before the aborted CPU run has returned. For a short new dictation, inline would save about 15–20 s once: 20 s of audio would take 6–8 s instead of 20–29 s. A long new dictation, for example the same text again, would run out of memory right after the return. Check 2 would then keep the engine on the CPU for the rest of the session, so every later dictation would run about 10 s slower. In the queued order, that dictation runs on the CPU and the engine still returns afterwards. Rejected, because the saving happens once and the cost can last the whole session. The queued order also keeps the rule from #9 that requests queued behind the failed one run on the CPU.

### D5: Logging

Without text or audio:
- Warning: the model is reloaded on Vulkan after an out-of-memory error on the given audio duration.
- Information: the engine stays on the CPU, with the reason: either the audio isn't longer than the longest run that completed on Vulkan (both durations), or the engine ran out of memory again after a return.

The existing lines log the load and warm-up durations and "ready on Vulkan".

## Risks / Trade-offs

- [No clip runs out of memory on the target laptop (Context). The return only helps GPUs with less memory, and none was tested.] → Accepted at the checkpoint: the forced limit produces the error, and the change is verified with it (`notes.md`).
- [The forced limit makes the KV cache fail after the encoder has run. On a GPU that really has less memory, a run may fail at another point, for example in the encoder.] → Accepted: every failure point surfaces as `ErrOom` from the run, which is all the transcriber sees.
- [During the return, `Ready` covers a reload of 3–5 s, and the tray still shows "Ready (CPU)". A request that arrives then waits up to that long, or longer if Vulkan fails and the engine falls back to the CPU.] → Accepted: the request then runs on the GPU, which saves more than the wait for all but the shortest clips. No request is rejected.
- [If a model or backend change replaces the return after it has released the CPU engine, the requests waiting for it are rejected as still loading, although they were submitted before the change.] → Accepted, like `retry-transcription-after-backend-failure` D5: it needs a settings change within the 3–5 s of the return, while a request waits.
- [Every long dictation that runs out of memory again pays for the failed Vulkan attempt, the reload on the CPU, the CPU run and the reload on Vulkan.] → Accepted: short dictations, which are more frequent, get the GPU back. The reload on Vulkan uses idle time unless a dictation follows at once. D1 showed that the failed attempt takes its whole encoder pass, so skipping it (option B) is a follow-up.
- [Two long dictations in a row, with no successful Vulkan run between them, leave the engine on the CPU (check 2).] → Accepted: that's today's behavior, and a restart or settings change resets it.
- [After a return, a clip that fitted before may not fit anymore, for example because another app now uses more of the shared memory. Check 1 then keeps the engine on the CPU although the clip length caused the error.] → Accepted: the engine stays on the CPU, as today.

## Open Questions

- Is option B (sending clips above a learned length straight to the CPU) worth a follow-up? D1 found that a failed Vulkan run returns only after its whole encoder pass: 117 s for 240 s of audio under the 32 MiB cap. That favors the follow-up, and doesn't change this design.
