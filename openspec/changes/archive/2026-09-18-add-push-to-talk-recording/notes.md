# Notes

## Audio hardware verification (tasks 3.1 and 3.6), 2026-09-18

**Machine:** Lenovo 21SX (ThinkPad E14 Gen 7). Default recording device: *Mikrofonarray (Intel® Smart Sound Technologie für digitale Mikrofone)*. NAudio.Wasapi 3.1.0, default-device routing, 16 kHz mono float, 20 ms buffer requested.

### Spike baseline (3.1), all three privacy switches on

`MicrophoneAccessSpikeTests.Spike_RecordTwoSeconds_ReportsOutcome` with *Microphone access*, *Let apps access your microphone* and *Let desktop apps access your microphone* all `Allow`:

- The 16 kHz mono float format is accepted (`32 bit IEEEFloat: 16000Hz 1 channels`).
- 2 s from `StartRecording` gave 396 packets and 31,649 samples; no packet carried the WASAPI silent flag.
- `AudioEndpointVolume.Mute` of the default capture endpoint reads `False` (mute key off).

### Privacy switches off (3.1)

One switch off at a time, the others `Allow`:

| Switch off | Registry `Value` | Outcome |
|---|---|---|
| *Microphone access* (device) | HKLM `Deny` | The build succeeds; `StartRecording()` throws `CoreAudioException` with HRESULT `0x80070005` (`E_ACCESSDENIED`). No packets. |
| *Let apps access your microphone* | HKCU `Deny` | Same: `StartRecording()` throws `CoreAudioException`, `0x80070005`. No packets. |
| *Let desktop apps access your microphone* | HKCU `NonPackaged` `Deny` | Same: `StartRecording()` throws `CoreAudioException`, `0x80070005`. No packets. |

**Conclusion:** all three switches fail the start with `E_ACCESSDENIED`, which the error mapping in D5 turns into `MicrophoneAccessDeniedException`. No switch delivers silence, so the `ConsentStore` check is not needed.

The first try with *Microphone access* is not counted. Settings showed the switch as off, but the HKLM value still read `Allow` and speech came through (peak 0.0765). After Settings was reopened, the value read `Deny`. Check the registry values that the spike prints before trusting a run.

**Mute key:** with the laptop's microphone mute key on, `AudioEndpointVolume.Mute` of the default capture endpoint reads `True`, so the mute check in D5 works as designed.

The spike test was deleted after these runs.

### Packet timeline at start

Temporary diagnostic on `WasapiCaptureSession`, three starts in a row, 2.5 s each:

| Run | `Start()` returned | First packet | First non-zero audio | Audio delivered |
|---|---|---|---|---|
| 0 | 269 ms | 298 ms | ~300–550 ms | 2,373 ms |
| 1 | 194 ms | 216 ms | ~600–850 ms | 2,138 ms |
| 2 | 610 ms | 634 ms | ~1,000–1,235 ms | 2,113 ms |

- `WasapiRecorder.StartRecording()` blocks for 190–610 ms.
- Packets hold 160 samples (10 ms), not the requested 20 ms.
- The first packets contain only digital zeros (peak 0.000) but are **not** flagged silent. In runs 1 and 2, delivery then pauses for about 370 ms after the first 2–3 packets. The pause is not made up later: afterwards, packets arrive in real time.
- No packets arrived after the stop request.

### Hardware tests (3.6), first run, before the D5 revision

| Test | Result |
|---|---|
| 2 s recording | **Failed:** 22,797 samples (1.42 s) instead of about 32,000. The start completes on the first zero packet, before the pause described above, so about 0.6 s of the 2 s wait delivers no audio. All samples within [-1, 1]; peak 0.125. |
| `StartAsync` duration (5 starts) | 614, 528, 220, 205, 238 ms, all over the 200 ms target. This is the time to the first packet not flagged silent; real audio starts about 0.4 s later (see the timeline). |
| 50 start/stop cycles | **Failed:** handles 477 → 602. |
| Abort | Passed. |

**Handle count:** not a leak. 200 `AudioRecorder` cycles show one step of about +130 handles between cycles 50 and 75, then a flat or falling count (589 → 580 at cycle 200). Isolated runs: the mute check and building and disposing a recorder stay flat; the step appears only with start and stop.

These results led to the D5 and D6 revisions: leading all-zero packets now count as silence, the latency is accepted for now, and the handle test warms up with 100 cycles.

### Hardware tests (3.6) after the D5 revision

| Test | Result |
|---|---|
| 2 s recording | Passed: 32,160 samples in the full class run. Five separate runs gave 32,160–32,640. One earlier run, the first test in a fresh process, gave 36,980 (2.31 s) and did not repeat. In a warm process, five recordings matched the 2 s wall time within 1 %. |
| `StartAsync` duration (5 starts), now the time to real audio | 701, 612, 375, 515, 956 ms. Earlier run: 323, 646, 448, 977, 468 ms. All over the 200 ms target; accepted in D6. |
| 100 warm-up cycles, then 50 cycles | Passed: handles 598 → 594. |
| Abort | Passed. |

**Noise floor:** in a quiet room, the array's output after the DSP wakes up peaks at about 0.0001–0.0002 (about −74 dBFS), but 95–100 % of the samples are non-zero. The zero check therefore does not wait for speech once the device delivers audio.

## Manual error checks (task 3.7), 2026-09-18

Run with `AudioRecorderHardwareTests.StopAsync_AfterTwoSeconds_ReturnsAbout32000SamplesInRange`, which goes through `AudioRecorder` and `WasapiCaptureSessionFactory`.

| Check | Outcome |
|---|---|
| (e) Mute key on | `StartAsync` fails after 61 ms with `MicrophoneMutedException` from the mute check, before a recorder is built. Windows' microphone usage database (`CapabilityAccessManager.db-wal`) was not written during the run; it was written by the earlier spike run that recorded audio. No microphone-in-use indicator appeared in the taskbar. |
| (a) *Microphone access* off (HKLM `Deny`) | `StartAsync` fails after 140 ms with `MicrophoneAccessDeniedException`, wrapping `CoreAudioException` `0x80070005` from `WasapiCaptureSession.Start()`. |
| (a) *Let apps access your microphone* off (HKCU `Deny`) | Same: `MicrophoneAccessDeniedException` after 118 ms. |
| (a) *Let desktop apps access your microphone* off (HKCU `NonPackaged` `Deny`) | Same: `MicrophoneAccessDeniedException` after 110 ms. |
| (b) All input devices disabled in Settings › System › Sound | No capture endpoint is active (the microphone array's `DeviceState` has the disabled bit set; the others are not present or unplugged). `StartAsync` fails after 88 ms with `NoMicrophoneException`, raised by the default endpoint lookup before a recorder is built. |

**Harness for (c) and (d):** the temporary explicit test `DeviceChangeManualTests.Record_FortySeconds_ReportsAudioFlow` (20 s at first, then 40 s to give more time for a manual switch) records through `AudioRecorder` and reports, every 2 s, the samples the session delivered, their peak and the default device. Baseline without a device change, quiet room: 31,716–33,889 samples per 2 s window, the start after 0.5 s, and `StopAsync` returning 322,116 samples (20.13 s).

**Observation:** in that baseline, window peaks were between 0 and 3.2e-5, below the noise floor measured earlier (about 1e-4), and four of the ten windows were exact digital zeros for 2 s. The input level was 100 % and the endpoint not muted. The array's DSP apparently gates a silent room to zeros after the start. The start itself completed on non-zero audio after 0.5 s, so this run was not affected. If the gate is already closed when a recording starts, the start would wait until the user speaks, and in a silent room it would time out with `MicrophoneNotRespondingException`. Revisit with `add-dictation-workflow`, where the "Recording" cue waits for the start.

**(c) attempts:** a USB headset (*Jabra Evolve 10*) became the default recording device as soon as it was connected. In two runs, one of 20 s and one of 40 s, the default did not change during the recording. **(c) was then skipped by decision and is not verified.** Recording from the headset was steady: 31,716–32,604 samples per 2 s window, the start after 0.2–0.4 s, and `StopAsync` returning 40.26 s for the 40 s run.

**Out-of-range samples:** in the 40 s run, the windows ending at 36.6 s and 38.6 s peaked at 0.182 and **1.32**. The session delivers the samples Windows converts to 16 kHz mono float, and `SampleAccumulator.Append` copies them unchanged, so the clip held samples outside [-1, 1]. This breaks the *Output format* requirement. Float conversion does not clamp, so a loud peak in the device's native format can overshoot, most likely in the resampler. **Fixed:** `SampleAccumulator.Append` now clamps each sample to [-1, 1] (design D5, task 3.3), covered by a unit test.

**(d) Only microphone unplugged:** the microphone array was disabled, so the Jabra headset was the only input. It was unplugged about 23 s into the 40 s run. Audio flowed normally until then, and the last window before the failure delivered 12,960 samples (0.81 s). At 23.6 s, about 0.4 s after the last audio, `Failed` was raised with `MicrophoneDisconnectedException`, wrapping `CoreAudioException` with HRESULT `0x80070490` (`E_NOTFOUND`). From then on, `IsRecording` was `false`, no default capture device existed, no samples arrived, and `StopAsync` threw `MicrophoneDisconnectedException`. Capture does not stall, so the D5 open point is answered: no watchdog for missing samples is needed.

The harness test was deleted after these runs.

**(f) Bluetooth headset:** not done, because no Bluetooth headset was available. The Bluetooth start latency is still unmeasured.

Task 3.7 was ticked by decision, with (c) skipped and (f) not done.

## Push-to-talk hotkey (tasks 2.2 and 2.3), 2026-09-18

The app ran from `dotnet run` (Debug build, Debug log level), and the model loaded on Vulkan. The hook started without an error.

**2.2 in Notepad:** holding right Ctrl logged `Pressed` and, after 7.6 s, `Released`, with no false cancel from the missed-release check (about 30 ticks). Right Ctrl+C logged `Pressed` and then `Cancelled`, with no `Released` when right Ctrl was let go, and Notepad still copied the selection. The log held only the startup lines and these signals, with no key names, key codes or typed text.

**UAC is off on this laptop:** `EnableLUA` is `0`. Every process, including Explorer, Notepad, the app and a Windows Terminal titled "Administrator", runs with the full token at High integrity (`0x3000`, elevation type *default*). No window has a higher integrity than the app, so UIPI never hides key events from the hook.

- **2.3 (a) elevated Windows Terminal:** two runs logged `Pressed` → `Released`: the hook saw the release in the "Administrator" terminal. This is correct behavior for this configuration, but the missed-release case cannot be reproduced here.
- **2.3 (b) UAC prompt:** with `EnableLUA` `0`, `Start-Process -Verb RunAs` shows no prompt, so this case cannot be reproduced here either.
- **2.3 (c) lock screen:** the first try logged nothing, because right Ctrl went down only once the lock screen was coming up; a press afterwards showed the hook still worked. The second try, with `Start-Sleep 5`, logged `Pressed` before the lock and `Cancelled` when the lock screen appeared, at 12:52:12.741. No missed-release line preceded that cancel, so it came from a non-hotkey key-down the hook saw at the desktop switch, not from the missed-release timer. After the unlock, a right Ctrl key-down at 12:52:18.421 raised `Pressed` without a key-up, and 0.5 s later the missed-release check reset it and raised `Cancelled`. Whether that key-down was a real press whose key-up was lost or one Windows generated was not resolved. The following presses logged `Pressed` and `Released` normally. The requirement holds: locking cancels a held hotkey, and presses after the unlock work.

Task 2.3 was ticked by decision, with (a) and (b) not reproducible on this laptop and (c) passed. The unit tests cover the missed-release logic.

**2.2 endpoint protection:** the active endpoint protection is FortiClient; Microsoft Defender is not running. The app ran with the hook for 26 minutes (12:31–12:57) of normal work, including all hotkey checks above, instead of about an hour. Afterwards, the FortiClient console showed no detections, its quarantine folder was empty, `uiohook.dll` was still in the build output, Defender had no detections today, and the app log held no warnings or errors. Task 2.2 was ticked by decision after the shorter run.
