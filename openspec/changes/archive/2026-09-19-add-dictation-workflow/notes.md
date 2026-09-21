# Notes

**Machine:** Lenovo 21SX (ThinkPad E14 Gen 7), the target laptop.

## Automated verification (tasks 2.1 and 3.2), 2026-09-19

- `dotnet build Pisum.Transcribe.slnx`: 0 warnings, 0 errors.
- `dotnet test Pisum.Transcribe.slnx`: 300 passed, 0 failed, 23 explicit tests skipped. This includes the new `TextInserterTests` for the background restore, `TextInsertionServiceCollectionExtensionsTests` and the `RecordingOverlayWindowTests` placement tests.
- `TextInserterHardwareTests.InsertAsync_PasteIntoFocusedTextBox_InsertsTextExactlyAndRestoresClipboard` with `--explicit on`: passed. The clipboard held "invoice 4711" after `StopAsync` waited for the pending restore.

Task 2.1 was ticked after these runs. The manual overlay checks of task 3.2 follow.

## How to run the manual checks

Start the app with `dotnet run --project src/Pisum.Transcribe`. The Debug build logs at Debug level, so the log in `%LOCALAPPDATA%\Pisum Transcribe\logs\` shows `Push-to-talk Released`, `Ran Translate from de to en … in <time>` and `Pasted <n> characters` with millisecond timestamps. The release-to-text latency is the time from the `Released` line to the `Pasted` line.

## Tray icon bug found in the first manual run, 2026-09-19

In the first run (10:03–10:05), the overlay stayed on "Transcribing…" after the text was pasted. The log showed `ObjectDisposedException` for `Icon` from `TrayIconService.SetStatus`, called by `DictationFeedback.Render()` in `ShowIdle`. H.NotifyIcon 2.4.1 disposes the icon it replaces (`OnIconChanged` calls `oldValue?.Dispose()`), and the icon it holds when it is disposed. `DictationFeedback` reuses the four `DictationIcons`, so after the first dictation the ready icon was disposed. The exception in the dispatcher callback skipped `Overlay.Hide()`.

**Fixed:** `TrayIconService.SetStatus` gives H.NotifyIcon a copy of the icon, so the caller keeps ownership. The new `TrayIconServiceTests` fail with the same exception without the fix. Afterwards, `dotnet test` gave 302 passed, 0 failed, 23 skipped. The runs from 10:08 on used the fix and logged no errors.

## Overlay (task 3.2, manual part)

| Check | Expected | Result |
|---|---|---|
| Overlay on the target window's monitor | Notepad on the second monitor: the pill appears at the bottom center of that monitor's work area, and likewise on the primary monitor. Mixed scaling, if available: correct size and position on both. | ok |
| Editor keeps its caret | While the overlay shows, Notepad stays the foreground window and its caret keeps blinking. | ok |
| Clicks pass through | A click on the pill reaches the window beneath it. | ok |
| Absent from Alt+Tab and the taskbar | Alt+Tab while recording shows no "Pisum Transcribe" entry, and the taskbar shows no button. | ok |
| Transparency | The pill has rounded corners with no black background or flicker on the Intel Arc iGPU (design risk: the fallback is `AllowsTransparency=false`). | ok |

## MVP run (task 4.1), 2026-09-19

Default settings: translate de→en, right Ctrl, `canary-1b-v2-q8_0` on Vulkan. Dictate a German sentence into each target.

| Target | English text inserted |
|---|---|
| Notepad | Good morning. |
| Browser text area | Good morning. |
| Windows Terminal | Good morning. |
| Word | Good morning. |

**Latency from the log:** the log does not name the target window, so the times are per dictation, not per target. The run from 10:14 to 10:16 had seven dictations, among them the four "Good morning." pastes of 13 characters:

| Released | Audio | Transcription | Release to text |
|---|---|---|---|
| 10:14:35.277 | 2.65 s | 282 ms | 388 ms |
| 10:14:42.268 | 2.04 s | 309 ms | 356 ms |
| 10:15:16.900 | 1.90 s | 252 ms | 309 ms |
| 10:15:34.788 | 1.56 s | 343 ms | 399 ms |
| 10:16:00.957 | 1.91 s | 265 ms | 349 ms |
| 10:16:05.085 | 1.92 s | 257 ms | 346 ms |
| 10:16:10.685 | 3.19 s | 252 ms | 342 ms |

The run at 10:09, right after the model loaded, had five dictations of 1.2–5.4 s of audio. Release to text was 292–608 ms. The slowest was the first dictation after the load, with 483 ms of transcription for 4.8 s of audio.

Across all 12 dictations, release to text was 292–608 ms, median 353 ms. Transcription took 248–483 ms of that, and stopping the recording plus inserting the text took 44–125 ms. Press to recording start was 318–513 ms, so the starting look shows for up to about half a second before "Recording".

## Edge cases (task 4.2)

| Check | Expected | Result |
|---|---|---|
| Tap under 300 ms | Nothing happens: no transcription, no insertion, no message. | |
| Right Ctrl+C while holding | Cancelled, nothing inserted, and the copy still works. | |
| Switch windows during transcription | Notification that the text was copied to the clipboard because the active window changed. | |
| Press in an elevated terminal | Nothing happens, because the hook does not see keys in elevated windows. UAC is off on this laptop (`EnableLUA` `0`, see `add-push-to-talk-recording` notes), so this may not be reproducible. | |
| Hold in Notepad, click into an elevated terminal | Cancelled, nothing inserted. Same UAC caveat. | |
| Hold right Ctrl and scroll the mouse | The dictation runs, and the overlay stays visible. | |
| Press during transcription | Overlay shows "Still processing…", no second recording, and the first dictation completes. | |
| Copy "invoice 4711", dictate, press again right after the text appears | A new recording starts without "Still processing…". After both dictations, the clipboard holds "invoice 4711". | |
| Copy "invoice 4711", dictate, choose **Exit** right after the text appears | The clipboard holds "invoice 4711" after the app has ended. | |
| Press while the model loads | One notification that the model is still loading, even for repeated presses. | |
| Microphone privacy off | Notification pointing to the microphone privacy settings; the next press retries. | |
| Microphone muted with the mute key | Notification to unmute the microphone. | |
| Bluetooth headset as the default recording device | Starting look until audio flows, then "Recording" from 0:00, and the first spoken words are transcribed. No Bluetooth headset was available for `add-push-to-talk-recording`. | |
| Silence | Overlay shows "No speech detected" for about 1.5 s, and nothing is inserted. | |
| Clipboard history (turn it on first in Settings › System › Clipboard, off again afterwards) | Win+V history does not contain the transcript, and text copied before the dictation appears in it once after the restore. | |

## Data retention (task 4.3)

After several dictations, search `%LOCALAPPDATA%\Pisum Transcribe\` including `logs\` for the dictated words, and look for audio files (`*.wav`, `*.pcm`, `*.raw`, `*.mp3`).

| Check | Expected | Result |
|---|---|---|
| Dictated words in the data folder and logs | No matches. | |
| Audio files | None. | |
