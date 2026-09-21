## Why

Pisum Transcribe runs NVIDIA Canary speech models on the user's machine. These GGUF models are 200 MB to 1.1 GB, so they are too large to bundle with the app. `docs/idea.md` recommends downloading them on first run from Hugging Face and verifying them with a checksum. Without an installed model, nothing else in the app can work, so this capability has to exist before the transcription engine.

## What Changes

- Add a built-in **model catalog** with three pinned Canary GGUF models. Each entry records the download URL at a fixed Hugging Face revision, the size, the SHA-256 hash, the supported languages, and the license and attribution.
  - `canary-1b-v2-q8_0`: Canary 1B v2, Q8_0, 1.07 GB, 25 European languages. This is the default.
  - `canary-1b-v2-q4_k_m`: Canary 1B v2, Q4_K_M, 701 MB, 25 European languages. Smaller.
  - `canary-180m-flash-q8_0`: Canary 180M Flash, Q8_0, 208 MB, en/de/es/fr. Fastest.
- Store downloaded models under `%LOCALAPPDATA%\Pisum Transcribe\models\`.
- Download with progress and cancellation. Check free disk space first, verify the SHA-256 hash after download, and install the file only after verification succeeds.
- Add a **first-run setup window**. It opens when the selected model is not installed, lets the user pick a catalog model and shows its size, languages and license. Starting a download makes the chosen model the selected model. Closing the window during a download asks for confirmation, and then cancels the download.
- Add a **Download model…** tray menu item for the moment when no model is installed, such as after the user closed the setup window. Whether the item is shown is checked each time the tray menu opens, so it also comes back when a model file is removed while the app runs.
- Add the `model.selectedModelId` setting (default `canary-1b-v2-q8_0`).

## Capabilities

### New Capabilities
- `model-management`: The catalog of supported speech models; download, verification and installation; installed-state detection; the first-run setup flow; the selected-model setting.

### Modified Capabilities
<!-- None. -->

## Impact

- New code: `src/Pisum.Transcribe/SpeechModels/` (catalog, model store, downloader, setup window), plus tray menu and settings additions.
- Changed code: `ITrayIconService.AddMenuItem` gets an optional visibility check that runs when the tray menu opens.
- New dependency: none beyond the BCL (`HttpClient`, `SHA256`), and `CommunityToolkit.Mvvm` for the setup window's view model.
- Network: HTTPS downloads from `huggingface.co`, which redirects them to its CDN under `hf.co`. These are the app's only network calls. Firewall allowlists need both.
- Disk: up to about 1.1 GB per model under `%LOCALAPPDATA%\Pisum Transcribe\models\`.
- Licensing: the Hugging Face GGUF repositories and NVIDIA's original model cards list the Canary weights as CC-BY-4.0, so the setup window shows attribution: the creator, the license with a link, and the changes made.
- Depends on `scaffold-app-shell`. `add-transcription-engine` depends on this change.
