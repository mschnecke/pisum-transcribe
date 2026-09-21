## Why

Converting speech into text is the core of Pisum Transcribe. `docs/idea.md` picks NVIDIA Canary models run locally through transcribe.cpp. Dictation needs a warm model: loading 1.1 GB per clip would take many seconds. It also needs a GPU path (Vulkan on Intel Xe) that falls back to CPU, and strict serialization, because transcribe.cpp 0.x allows only one compute call per model at a time. Recording and text insertion are useless until this works.

## What Changes

- Add a transcription service behind an `ITranscriber` abstraction, so the engine can later be swapped (e.g. sherpa-onnx) without touching dictation code.
- Implement it with `TranscribeCppSharp` 0.2.0 and `TranscribeCppSharp.Native.win-x64` 0.2.3, bound to transcribe.cpp v0.2.3 with CPU and Vulkan binaries. `docs/idea.md` assumed an older 0.1.3 binding, which is outdated.
- Load the selected model once in the background: at startup when the model is installed, or right after its download finishes. Keep it loaded, and run a warm-up inference before reporting ready.
- Backend preference **Auto** (default: Vulkan if usable, otherwise CPU), **Vulkan** or **CPU**. Auto falls back to CPU when Vulkan fails to load or to warm up.
- Handle errors during a transcription: a GPU backend error with **Auto** on Vulkan fails that request and reloads the model on CPU, so the next request works. With **Vulkan** or on CPU, it sets the status to failed. Output cut off at the model's output limit returns the partial text.
- Task and language settings: **translate** (default) or **transcribe**, source language (default `de`) and target language (default `en`), validated against the selected model's catalog languages.
- Process transcription requests one at a time in arrival order. Reject input longer than the model's maximum (about 400 s for Canary) before it reaches the native engine.
- Show the engine status (loading, ready with its backend, failed) in the tray tooltip. Notify the user when loading fails.
- Handle damaged model files: if the model fails to load and its hash no longer matches, delete the file so the setup window offers a new download.
- Add explicit benchmark and integration tests for German→English latency on Vulkan and CPU. This is the "Phase 0" spike threshold from `docs/idea.md`.

## Capabilities

### New Capabilities
- `transcription`: Loading and keeping a speech model warm, backend selection and fallback, task and language settings with validation, serialized transcription of 16 kHz mono audio, input limits, and status and failure reporting.

### Modified Capabilities
<!-- None. -->

## Impact

- New code: `src/Pisum.Transcribe/Transcription/`, and `ITrayIconService.SetToolTip`, which changes the tray tooltip without changing the icon.
- New dependencies: `TranscribeCppSharp` 0.2.0 and `TranscribeCppSharp.Native.win-x64` 0.2.3. These ship `transcribe.dll`, `ggml*.dll` and the Vulkan backend, all MIT licensed.
- Runtime: Vulkan requires the loader that current GPU drivers ship. The CPU backend ships builds from plain x64 up to AVX-512 and picks one at runtime. Memory use grows by about the model size while the app runs.
- Settings: new `transcription` section (`backend`, `task`, `sourceLanguage`, `targetLanguage`).
- Depends on `scaffold-app-shell` and `add-model-management`. `add-dictation-workflow` and `add-settings-window` depend on this change.
