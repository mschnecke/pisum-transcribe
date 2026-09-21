## Why

The previous changes deliver independent pieces: a warm transcription engine, a push-to-talk hotkey, microphone recording and text insertion. None of them is useful alone. This change connects them into the product's core loop from `docs/idea.md`: *hold the key, speak, release, and the text appears at the cursor*. It also adds the feedback a background tool needs so the user always knows what is happening: a recording overlay, tray icon states and error notifications. After this change, Pisum Transcribe is a usable MVP.

## What Changes

- Add a dictation controller that drives: hotkey pressed → record → hotkey released → transcribe with the configured task and languages → insert at the cursor of the window that was active when recording started.
- Handle the edge cases:
  - Presses shorter than 300 ms are discarded.
  - A cancelled hotkey (another key pressed while holding) discards the recording.
  - Recording stops automatically at the model's maximum input duration.
  - Presses while a previous dictation is still processing are ignored.
  - Presses while the model is not ready produce a notification explaining why.
- Trim the transcript and skip insertion when it is empty ("No speech detected").
- Add a small **recording overlay**. It is topmost, never takes focus, and ignores mouse clicks. It appears on the target window's monitor and shows "Recording" with elapsed time, then "Transcribing…" and short result hints.
- Add **tray icon states**: ready, recording, transcribing and unavailable, with matching tooltips. Every tooltip contains the product name. While no dictation runs, the tray shows the engine status.
- Turn recording, transcription and insertion failures and clipboard fallbacks into tray notifications with actionable text.
- Keep audio and transcripts in memory only, and discard them after each dictation.
- End a dictation once its text is delivered. Text insertion finishes the clipboard restore in the background, so the next dictation can start right away. The next insertion and the application exit wait for a pending restore, so the user's clipboard is always put back.

## Capabilities

### New Capabilities
- `dictation`: The end-to-end push-to-talk dictation flow. Covers state transitions, readiness and busy handling, minimum duration, cancellation, maximum duration, empty results, the recording overlay, tray states, user notifications and data retention.

### Modified Capabilities
- `transcription`: The tray tooltip reflects the engine status only while no dictation is in progress. During a dictation, it shows the dictation state.
- `text-insertion`: The clipboard restore no longer delays the end of an insertion. An insertion that starts while a restore is pending waits for it, and a pending restore finishes before the application exits.

The `audio-recording` and `push-to-talk-hotkey` capabilities are consumed unchanged.

## Impact

- New code: `src/Pisum.Transcribe/Dictation/` (controller, feedback adapter, overlay window, tray state icons drawn at startup).
- Uses: `ITranscriber`, `IAudioRecorder`, `IPushToTalkHotkey`, `IForegroundWindowTracker`, `ITextInserter`, `ISettingsStore` and `ITrayIconService`.
- Changed code:
  - `TextInsertion/TextInserter.cs` runs the clipboard restore in the background and becomes a hosted service, with its restore tests adjusted.
  - `Transcription/TranscriberHostedService.cs` hands the tray tooltip to the dictation feedback.
- App manifest: per-monitor DPI awareness (PerMonitorV2), so the overlay is positioned correctly on mixed-DPI setups.
- No new packages.
- Depends on `scaffold-app-shell`, `add-model-management`, `add-transcription-engine`, `add-push-to-talk-recording` and `add-text-insertion`.
