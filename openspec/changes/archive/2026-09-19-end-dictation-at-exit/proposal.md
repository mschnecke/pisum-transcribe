## Why

When the user chooses **Exit** from the tray during a dictation, the dictation does not end. During a recording, the overlay stays visible with its timer counting, and Windows keeps showing the microphone as in use until the process has ended, up to about 5 seconds later (issue #10). The overlay's starting look and its "Transcribing…" spinner also stay visible until then. A cancelled dictation already aborts the recording and hides the overlay at once, and an exit should do the same.

## What Changes

- When the application exits during a recording, the recording is aborted at once: the microphone is released, the audio is discarded, and nothing is transcribed or inserted.
- When the application exits in any phase of a dictation (starting, recording, transcribing, or while a short overlay message is shown), the overlay hides at once, before the transcription engine and the other background services stop.
- Ending the dictation at exit does not update the tray icon, which the shutdown has already removed.
- An exit that aborts a recording writes one Information log entry with the recording's duration, and no audio or text.
- Not included: Windows sign-out, shutdown or restart. In those cases WPF ends the application by itself, without the shutdown that **Exit** runs, so this change has no effect there. That is issue #13. Cancelling a running transcription by the user is #11.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `dictation`: new requirement "Dictation ends when the application exits".

## Impact

- Code: `src/Pisum.Transcribe/Dictation/DictationController.cs` (`StopAsync`), `src/Pisum.Transcribe/Dictation/DictationFeedback.cs` (`StopAsync`) and their unit tests. No new dependencies, settings or UI.
- User-visible: after **Exit** during a dictation, the overlay disappears and the Windows microphone-in-use indicator goes off at once, instead of when the process ends.
- Shutdown time: releasing the microphone now happens while the dictation controller stops instead of when the host is disposed. It is not new work, and the 4.5 s watchdog still bounds the exit.
- Privacy: the aborted recording's audio is discarded, as for a cancelled dictation. Nothing is written to disk or logs.
- Relates to issues #10, #6 (dictation workflow, MR !7) and #11.
