# Pisum Transcribe Roadmap

This roadmap covers v1 of Pisum Transcribe, the push-to-talk dictation app described in [idea.md](idea.md). Each step is one GitLab issue and one OpenSpec change in `openspec/changes/<change>/`. Implement a change with `/opsx:apply <change>`, and archive it with `/opsx:archive <change>` once it is done.

## Dependency graph

```mermaid
graph TD
    S1["#1 scaffold-app-shell"] --> S2["#2 add-model-management"]
    S1 --> S4["#4 add-push-to-talk-recording"]
    S2 --> S3["#3 add-transcription-engine"]
    S4 --> S5["#5 add-text-insertion"]
    S3 --> S6["#6 add-dictation-workflow"]
    S5 --> S6
    S6 --> S7["#7 add-settings-window"]
    S7 --> S8["#8 add-voice-activity-detection"]
    S3 --> S9["#9 retry-transcription-after-backend-failure"]
    S9 --> S11["#11 cancel-running-transcription"]
    S9 --> S12["#12 return-to-gpu-after-out-of-memory"]
```

## Order

| Step | Issue | OpenSpec change | Blocked by | Delivers |
|---|---|---|---|---|
| 1 | [#1](https://gitlab.com/pisum-projects/projects/whisper/transcribe/-/work_items/1) | `scaffold-app-shell` | – | WPF tray app, single instance, logging, settings storage, xunit v3 test project |
| 2 | [#2](https://gitlab.com/pisum-projects/projects/whisper/transcribe/-/work_items/2) | `add-model-management` | #1 | Canary model catalog, verified download, first-run setup window |
| 3 | [#3](https://gitlab.com/pisum-projects/projects/whisper/transcribe/-/work_items/3) | `add-transcription-engine` | #2 | Warm Canary engine, Vulkan→CPU fallback, German→English translation by default |
| 4 | [#4](https://gitlab.com/pisum-projects/projects/whisper/transcribe/-/work_items/4) | `add-push-to-talk-recording` | #1 | Global hold-to-talk hotkey (Right Ctrl), 16 kHz mono microphone recording |
| 5 | [#5](https://gitlab.com/pisum-projects/projects/whisper/transcribe/-/work_items/5) | `add-text-insertion` | #4 | Paste at cursor with clipboard restore, type-text fallback, elevated and changed window handling |
| 6 | [#6](https://gitlab.com/pisum-projects/projects/whisper/transcribe/-/work_items/6) | `add-dictation-workflow` | #3, #5 | **MVP:** hold, speak, release, text appears; overlay, tray states, notifications |
| 7 | [#7](https://gitlab.com/pisum-projects/projects/whisper/transcribe/-/work_items/7) | `add-settings-window` | #6 | Settings UI with live apply, model download and delete, autostart |
| 8 | [#8](https://gitlab.com/pisum-projects/projects/whisper/transcribe/-/work_items/8) | `add-voice-activity-detection` | #7 | Silero VAD silence trimming, no transcription for silent clips |
| 9 | [#9](https://gitlab.com/pisum-projects/projects/whisper/transcribe/-/work_items/9) | `retry-transcription-after-backend-failure` | #3 | A dictation that fails on the GPU is transcribed on the CPU instead of lost |
| 10 | [#11](https://gitlab.com/pisum-projects/projects/whisper/transcribe/-/work_items/11) | `cancel-running-transcription` | #9 | Cancel a dictation while it is transcribed |
| 11 | [#12](https://gitlab.com/pisum-projects/projects/whisper/transcribe/-/work_items/12) | `return-to-gpu-after-out-of-memory` | #9 | Back to the GPU after an out-of-memory error on a long dictation |

## Phases

### Phase 1: Foundation (#1)
Everything else builds on the scaffold, so it must come first.

### Phase 2: Two parallel tracks (#2 → #3, and #4 → #5)
After #1, two tracks have no dependency on each other:
- **Engine track:** #2 model management, then #3 transcription engine.
- **Input and output track:** #4 hotkey and recording, then #5 text insertion.

**Checkpoint after #3:** run the explicit benchmark tests on the target laptop (ThinkPad E14 Gen 7, Intel Xe). The target is warm transcription of a 10 s clip well under 3 s. If Vulkan is slower than CPU or unstable, or Q4_K_M matches Q8_0 in accuracy, change the default backend or model before #6.

### Phase 3: MVP (#6)
The dictation workflow joins both tracks. After #6, the app is usable end to end with the settings in `settings.json`.

### Phase 4: Polish (#7 → #8)
- #7 settings window makes every option configurable without editing JSON.
- #8 voice activity detection depends on #7 only for its settings checkbox.

### Phase 5: Hardening (#9 → #11, #12)
- #9 keeps a dictation when the GPU fails.
- #11 and #12 build on #9 and don't depend on each other.

## Deferred (not planned yet)

- Packaging and distribution: Velopack self-contained installer and auto-update, code signing, WinGet and Chocolatey manifests, GitLab CI build and test pipeline. This needs a Windows runner.
- Collect the license notices of the dependencies that already ship (NAudio, SharpHook, TranscribeCppSharp and ggml, H.NotifyIcon, CommunityToolkit.Mvvm, Serilog, Microsoft.Extensions) in `THIRD-PARTY-NOTICES.md`, which lists only Silero VAD and ONNX Runtime so far. Include the `ThirdPartyNotices.txt` that the ONNX Runtime package ships for the components it bundles.
- Confirm the Canary model license before public distribution. Hugging Face lists CC-BY-4.0, while transcribe.cpp's docs say Apache-2.0.
- Items listed as non-goals in the changes: microphone device picker, download resume, UI localization, sherpa-onnx engine.
