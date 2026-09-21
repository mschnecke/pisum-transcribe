## Why

Push-to-talk recordings usually start and end with silence, because users press the key before speaking and release it after a pause. Canary has no built-in voice activity detection (VAD): it spends compute on that silence, and on silent or near-silent clips it can hallucinate text. `docs/idea.md` recommends Silero VAD in front of the model, the same pipeline Handy uses, to lower latency and improve accuracy.

## What Changes

- Add voice activity detection with the Silero VAD model (v6, MIT license) on ONNX Runtime, bundled with the app. No download and no network access are needed.
- Before transcription, trim leading and trailing silence from each dictation, keeping 300 ms of padding around the detected speech. Pauses inside the speech are kept.
- When no speech is detected, skip transcription entirely. The existing "No speech detected" overlay is shown.
- If VAD fails, transcribe the untrimmed audio so dictation keeps working, and log a warning.
- Add the `voiceActivity.enabled` setting (default on), exposed in the settings window's Dictation section as "Trim silence before transcription".
- Warm up the VAD model at startup, so the first dictation pays no load cost.
- Rejected option: the third-party `SileroVad` and `ManySpeech.SileroVad` NuGet wrappers, which declare no license. Instead the app ports the MIT-licensed upstream C# example, about 150 lines.

## Capabilities

### New Capabilities
- `voice-activity-detection`: Detecting speech in dictation audio. Covers silence trimming before transcription, skipping silent recordings, failure fallback, the enable setting and its settings-window toggle, and the performance bound.

### Modified Capabilities
<!-- None. The dictation flow keeps its specified behavior; VAD only narrows the audio passed to transcription and reuses the existing empty-result feedback. The settings-window checkbox and when its change applies are specified in `voice-activity-detection`, so `settings-window` is not modified. -->

## Impact

- New code: `src/Pisum.Transcribe/VoiceActivity/` (detector, trimmer, bundled `silero_vad.onnx`, 2.3 MB), a hook in `DictationController` processing, and a checkbox in the settings window's Dictation section.
- New dependency: `Microsoft.ML.OnnxRuntime` 1.30.0 (CPU execution provider, MIT). It adds about 12 MB of native `onnxruntime.dll` to the output.
- Settings: new `voiceActivity` section (`enabled`).
- Licensing: add `THIRD-PARTY-NOTICES.md` with the MIT notices of the two components this change ships, Silero VAD and ONNX Runtime. Notices for the dependencies the app already ships are left to the deferred packaging work in `docs/roadmap.md`.
- Depends on `add-dictation-workflow` and `add-settings-window`.
