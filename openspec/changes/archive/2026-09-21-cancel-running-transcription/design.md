## Context

See proposal.md, Why. The behavior is specified in `specs/dictation/spec.md` and `specs/transcription/spec.md`.

`DictationController` owns all dictation state in one loop that reads an event channel. Once a recording stops, `ProcessAsync` runs as a tracked task: it trims the silence (`IVoiceActivityDetector.DetectSpeech`), awaits `ITranscriber.TranscribeAsync`, and awaits `ITextInserter.InsertAsync`. Its `finally` posts `ProcessingCompleted`, and the loop answers that with `EndDictation`, which sets `Idle` and calls `ShowIdle`. Every step gets the controller's app-lifetime token `_stopping`, which is cancelled only at **Exit**. The `OperationCanceledException` filters in `ProcessAsync` and `TrimSilence` test `_stopping.IsCancellationRequested`.

The engine already handles a caller that stops waiting (`retry-transcription-after-backend-failure`, D4):

| Where the request is | What `TranscribeCppTranscriber` does on the caller's token |
|---|---|
| Queued | `TranscribeAsync` returns at once through `WaitAsync`. The worker skips the item when it reaches it. |
| Running | `RunOnEngine` links the token into the native run, which aborts at its next abort check. |
| CPU reload after a Vulkan backend error | `Load` observes only `_stopping`, so the reload finishes. `Run` then sees the cancelled token and cancels the item instead of retrying. |

In transcribe.cpp v0.2.3, Canary checks for an abort before the encoder and between decode steps. The encoder runs over the whole clip as one graph, and nothing can interrupt it.

`DictationFeedback` is the only writer of the tray icon and tooltip. It tracks the phase (`Idle`, `Recording`, `Transcribing`) on the UI thread. `ITrayIconService.AddMenuItem(header, onClick, isVisible)` adds an item above **Exit** in registration order, and calls `isVisible` on the UI thread each time the menu opens.

## Goals / Non-Goals

**Goals:**
- One way to end a dictation: a cancel only cancels a token, and the existing `ProcessingCompleted` path ends the dictation.
- Tray code stays in `DictationFeedback`. The controller stays free of UI code.
- No change to `TranscribeCppTranscriber` or `ITranscriber`.

**Non-Goals:**
- Making the encoder pass interruptible. That needs a change in transcribe.cpp and a new `TranscribeCppSharp.Native` package.
- Showing that the engine is still finishing a cancelled run.

## Decisions

### D1: A cancellation token per dictation, linked to `_stopping`

When `StopRecordingAsync` moves to `Processing`, the controller creates `CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token)` and keeps it in a loop-owned field. `ProcessAsync` passes its token to `DetectSpeech` (through `TrimSilence`) and to `TranscribeAsync`. `InsertAsync` keeps `_stopping.Token`, so a cancel has no effect once the transcript is ready. The loop disposes the source when it handles `ProcessingCompleted`, after the processing task has ended.

The exception filters in `ProcessAsync` and `TrimSilence` test the dictation token instead of `_stopping`, so they cover both **Exit** and a cancel. Without that, a cancel would reach the catch-all in `ProcessAsync` and show "Dictation failed", and `TrimSilence` would log a false "detection failed" warning and go on to transcribe.

- *Alternative: pass a new token only to `TranscribeAsync`.* A cancel during the silence trimming would then wait for it to finish, and the filters would still need to change.

### D2: A cancel only cancels the token, and `ProcessingCompleted` ends the dictation

A new event kind, `CancelRequested`, is handled only in `(CancelRequested, State.Processing)`: the loop calls `CancelAsync` on the dictation's source and does nothing else. `CancelAsync` runs the token's callbacks on the thread pool, so the rest of `ProcessAsync` doesn't run on the loop. `ProcessAsync` then returns within milliseconds:
- `TranscribeAsync` returns at once through `WaitAsync`.
- `DetectSpeech` checks the token between 32 ms windows.

Its `finally` posts `ProcessingCompleted`, and the loop ends the dictation as today. In every other state, the event is dropped like any other event that lost a race. That covers a menu opened during one dictation and clicked after it ended.

- *Alternative: `EndDictation` directly in the cancel handler.* The old `ProcessAsync` would still post `ProcessingCompleted`. If the user had started a new dictation by then and it had reached `Processing`, that event would end the new dictation early. Avoiding that would need a dictation ID on every event.

### D3: `DictationFeedback` owns the menu item and raises an event

`IDictationFeedback` gets an event `CancelRequested`. `DictationFeedback.StartAsync` adds the menu item on the UI thread with the header `DictationMessages.CancelTranscriptionMenuItem` ("Cancel transcription"). The item is visible when `_phase == Phase.Transcribing`, and choosing it raises the event. The controller subscribes in `StartAsync` and unsubscribes in `StopAsync`, like the hotkey events, and posts `CancelRequested` into its channel.

The visibility check reads a field that the UI thread already owns, so it is cheap. It also matches what the tray icon shows. The phase stays `Transcribing` during insertion, so the item can be visible then too. A click during insertion does nothing (D1).

Because `AddDictation` is registered between `AddSpeechModels` and `AddSettingsWindow`, the menu reads **Download model…** (when shown), **Cancel transcription** (when shown), **Settings…**, then **Exit**.

- *Alternative: the controller adds the item.* It would need `ITrayIconService` and a thread-safe copy of its loop-owned state for `isVisible`, and it would become a second writer of the tray.

### D4: Log the cancel with durations only

When `ProcessAsync` ends through a cancel and `_stopping` is not cancelled, it writes one Information entry: "The dictation was cancelled after {ProcessingSeconds} s of processing, {AudioSeconds} s of audio". An exit stays silent, as today. No text is logged, and the cancelled request produces no transcript.

### D5: Measure the time a cancelled CPU run takes to return

A `Hardware` test in `TranscribeCppBenchmarkTests` loads the default model on the CPU backend. It builds 60, 120 and 399 s clips by repeating the German clip from `HardwareTestAssets`, because the model rejects 400 s as 5001 encoder frames, one more than it accepts. It cancels each run 100 ms after it started, and measures how long `Run` takes to return. That time is about one full encoder pass, the worst case for the next dictation. Like the latency table, the results go to the diagnostic messages, and the test asserts nothing about speed. The numbers go into the risk below after the first run.

## Risks / Trade-offs

- [After a cancel, the worker stays busy until the native call reaches its next abort check. If the cancel comes during the encoder pass of a long clip on the CPU, the next dictation's transcription waits for the rest of that pass. The next recording is not affected, so the extra wait is the rest of the pass minus the length of the new recording.] → Accepted (proposal). D5 measures it. On the target laptop, with `canary-1b-v2-q8_0` on the CPU backend and a run cancelled 100 ms after its start, `Run` returned after 19.3 s for 60 s of audio, 43.5 s for 120 s and 214.1 s for 399 s. The clip was synthesized German speech, repeated; the pass length depends on the audio length, not on what is said. If the wait is long, an interruptible encoder is an upstream request to transcribe.cpp, for example by connecting ggml's CPU abort callback.
- [The cancelled request keeps its samples in memory until the worker drops it, which can be after the native call returns.] → The same as at **Exit** today (`retry-transcription-after-backend-failure`, D4). Nothing is written to disk or logs.
- [Opening the tray menu probably moves the focus away from the target window. If the user opens it during a transcription and closes it without cancelling, the insertion may fall back to the clipboard with "the active window changed".] → The same happens today when the user opens **Settings…** during a transcription. It is not verified, and it doesn't affect a cancel, which inserts nothing.
- [A "Still processing…" message shown just before the cancel keeps the overlay visible until the message ends, at most 1.5 s.] → `ShowIdle` already lets a shown message end before hiding. Pressing the hotkey and choosing the menu item within 1.5 s is unlikely, and the tray already shows the ready state.
- [After a cancel during the CPU reload, the next press gets "The model is still loading."] → Intended (spec, "Cancel a transcription"). The tray shows the loading state until the reload has finished.
