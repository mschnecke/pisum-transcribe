# Notes

**Machine:** Lenovo 21SX (ThinkPad E14 Gen 7), Intel Core Ultra 7 255H, the target laptop. On AC power, Windows power plan "Balanced".

**Audio:** synthetic German speech from the Windows SAPI voice Microsoft Hedda, 16 kHz mono, 12.7 s: "Guten Morgen. Heute besprechen wir die Planung für das nächste Quartal. Bitte schicken Sie mir bis Freitag Ihre Zahlen, damit wir die Präsentation vorbereiten können." `PISUM_TRANSCRIBE_TEST_AUDIO` pointed to it. Real recordings may behave differently.

## Hardware tests (task 2.4), 2026-09-19

Run with `Pisum.Transcribe.Tests.exe -explicit only -class "*.SileroVoiceActivityDetectorHardwareTests" -diagnostics` (Debug build). All three tests passed in three runs.

| Test | Result |
|---|---|
| Clip padded with 2 s of silence on each side | Speech found at 96–11,936 ms of the original, in 4 segments. The padded clip (16,665 ms) was trimmed to 12,408 ms, 32 ms off the expected 12,440 ms. |
| Clip attenuated by −20 dB | Speech found. |
| 30 s of audio (the clip repeated), 5 runs after loading | Median 151 ms, 116 ms and 107 ms in the three runs. Single runs took 103–185 ms. |

The median stays well under the 300 ms bound, so the two-ended scan fallback from the design's risks is not needed.

**Deviation in the padded-clip test:** the task compares the trimmed padded clip with the trimmed original clip. In this clip the speech starts 96 ms into the original, so trimming the original clamps its 300 ms padding at the first sample, while the padded clip keeps the full padding. The trimmed original would be 12,236 ms, about 170 ms shorter than the trimmed padded clip, although the added silence is removed completely. The test therefore expects the original's speech (first start to last end) plus 300 ms on each side, which equals the trimmed original whenever the original has at least 300 ms of silence at both ends.

## Automated verification (tasks 3.1 and 3.2), 2026-09-19

- `dotnet build Pisum.Transcribe.slnx`: 0 warnings, 0 errors.
- `dotnet test Pisum.Transcribe.slnx`: 436 passed, 0 failed, 26 explicit tests skipped.
- The manual part of task 3.2 ("the checkbox shows the saved value") is covered by `SettingsDialogTests.Constructor_SavedVoiceActivity_ShowsItAsTrimSilenceAndWritesClicksBack`. The test builds the real `SettingsDialog`, finds the checkbox, and checks that it shows a saved `true` and `false` and writes a click back to the view model.

## App startup, 2026-09-19

The Debug build logged `Voice activity detection is ready after 1265 ms on ONNX Runtime 1.30.0` at 13:40:57.021, 1.5 s after `Hosting starting`. The process loaded `onnxruntime.dll` from the app folder, not the Windows ML copy (1.17) in `C:\Windows\System32`. The Canary model was ready 4 s later, at 13:41:01.054, so the warm-up had finished before the first possible dictation. The time from `Hosting starting` to `Loading model` was 1.5 s, within the 0.2–1.6 s of the earlier runs that day.

## Manual checks (task 3.3), 2026-09-19

Default settings: translate de→en, right Ctrl, `canary-1b-v2-q8_0` on Vulkan. The log in `%LOCALAPPDATA%\Pisum Transcribe\logs\` shows `Voice activity detection trimmed <original> s of audio to <trimmed> s in <time> ms` or `Voice activity detection found no speech …` for each dictation, and `Ran Translate from de to en …` when transcription runs.

| Check | Expected | Result |
|---|---|---|
| Press, wait 2 s, speak, wait 2 s, release | Text inserted. The log shows the trimmed duration shorter than the original. | ok |
| Hold 3 s silently | "No speech detected", and no `Ran …` line in the log. | ok |
| Hold 3 s silently next to a running fan | "No speech detected". | ok |
| Say only "Ja", with about 1 s of silence before and after | The word is inserted. If it is dropped, lower `SileroSpeechDetector.MinSpeechSamples` and repeat the silent hold. | ok |
| Settings window, Dictation section | "Trim silence before transcription" is checked. | ok |
| Clear the checkbox, save, and hold 3 s silently | Transcription runs: a `Ran …` line, and no voice activity line. | ok |
