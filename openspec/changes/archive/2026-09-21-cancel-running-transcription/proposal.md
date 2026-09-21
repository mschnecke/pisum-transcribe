## Why

Once the user releases the hotkey, a dictation can't be stopped until its text arrives, other than by **Exit**, which ends the app (issue #11). A new press only shows "Still processing…". On the CPU backend the default model `canary-1b-v2-q8_0` runs slower than real time (15.3 s for 10.7 s of audio), so a dictation of up to 400 s can keep the user waiting for minutes. This happens with backend `cpu` on every dictation, and with backend `auto` after a Vulkan backend failure, when #9 runs the failed dictation again on the CPU and lists the long wait as an accepted risk with cancelling as the follow-up.

## What Changes

- A **Cancel transcription** item in the tray menu, shown only while a dictation is being transcribed. Choosing it cancels the dictation: the audio is discarded, nothing is inserted, the overlay hides at once without a message, the tray returns to the engine status, and no notification is shown.
- A dictation can be cancelled from the end of its recording until its transcript is ready: during voice activity detection, while its request waits for the engine, while the native engine runs, and while the engine reloads the model on the CPU after a Vulkan backend failure. Once the transcript is ready, the insertion always finishes, and a late choice of the item has no effect.
- After a cancel, the next hotkey press is handled like any other press at the engine's current status. It starts a new recording when the engine is ready. During a CPU reload it gets the existing "model is still loading" notification.
- Cancelling doesn't change the engine status or the loaded model. A CPU reload in progress continues, and the cancelled request isn't run again.
- The running native transcription is aborted at its next abort check. In the pinned transcribe.cpp v0.2.3, Canary's encoder pass can't be interrupted, so the next dictation's transcription can wait until that pass ends. The next recording can start at once. A hardware test measures the wait for long clips on the CPU.
- A cancel writes one Information log entry with durations only, and no audio or text.
- Not included: returning partial text from a cancelled transcription, changes to cancelling during a recording, a hint in the overlay that points to the tray item, a hotkey or overlay way to cancel, and an interruptible encoder (an upstream change to transcribe.cpp).

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `dictation`: new requirement "Cancel a transcription"; "Busy handling" counts a cancelled dictation as finished. "Cancelled dictation" stays as is and still covers the cancel during a recording.
- `transcription`: new requirement "Request cancelled by the caller", which specifies the engine's existing behavior when a caller stops waiting.

## Impact

- Code: `src/Pisum.Transcribe/Dictation/DictationController.cs` (a cancellation token per dictation, a cancel event, the exception filters in `ProcessAsync` and `TrimSilence`), `src/Pisum.Transcribe/Dictation/DictationFeedback.cs` (the tray menu item and its visibility), `src/Pisum.Transcribe/Dictation/DictationMessages.cs` (the item's text), and their unit tests. `TranscribeCppTranscriber` doesn't change; it gets unit tests for the caller cancellation and a hardware test for the time a cancelled CPU run takes to return. No new dependencies or settings.
- User-visible: the tray menu shows **Cancel transcription** while a dictation is being transcribed.
- Engine: a cancelled run keeps the worker busy until its native call returns, so a queued request behind it can wait for the rest of an encoder pass. The audio of a cancelled request stays in memory until then, as it does at **Exit**.
- Privacy: nothing about the cancelled dictation is written to disk or logs beyond durations.
- Relates to issues #11, #9 (`retry-transcription-after-backend-failure`, MR !10), #6 and #10.
