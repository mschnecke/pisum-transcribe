# Pisum Transcribe

Pisum Transcribe is a push-to-talk dictation app for Windows. It runs in the system tray. Hold the hotkey, speak, and release it: the app transcribes your speech and inserts the text at the cursor in the active window. By default it translates German speech into English text.

Speech recognition runs on your computer with an NVIDIA Canary model and the [transcribe.cpp](https://github.com/handy-computer/transcribe.cpp) engine. Audio and text never leave the machine. The app goes online only to download a speech model.

## Features

- **Hold to talk:** a global hotkey (Right Ctrl by default) records while you hold it, in any application.
- **Translate or transcribe:** translate into another language (German to English by default), or transcribe in the spoken language. The Canary 1B v2 models support 25 European languages.
- **Text at the cursor:** the text is pasted through the clipboard, and the previous clipboard contents are put back. Typing the text as keyboard input is an option.
- **Local and fast:** the model stays loaded between dictations. It runs on the GPU through Vulkan and falls back to the CPU when the GPU fails.
- **Silence trimming:** [Silero VAD](https://github.com/snakers4/silero-vad) cuts the silence before and after your speech, and recordings without speech are not transcribed.
- **Feedback:** an overlay shows the recording time and the transcription progress, and the tray icon shows the app state.

## Requirements

- Windows 10 or 11, x64
- A microphone
- A GPU with a Vulkan driver is optional. Without one, the model runs on the CPU.
- 0.2 to 1.1 GB of disk space for a speech model

To build from source, you need the [.NET SDK 10.0.400](https://dotnet.microsoft.com/download) or a later feature band that `global.json` allows.

## Getting started

There is no installer yet. Build and start the app from source:

```sh
git clone git@gitlab.pisum:pisum-projects/projects/whisper/transcribe.git
cd transcribe
dotnet run --project src/Pisum.Transcribe
```

On the first start, the app opens the window **Download a speech model**. Choose a model and download it. The download is checked against its SHA-256 hash, and the model loads when it is complete. The tray tooltip shows **Ready (Vulkan)** or **Ready (CPU)** when you can dictate.

Only one instance runs at a time. A second start waits up to 6 seconds for the first instance to exit and then exits itself.

## Usage

1. Click into the window where you want the text, such as an editor or a chat.
2. Hold the hotkey and speak. The overlay shows **Recording** and the elapsed time.
3. Release the hotkey. The overlay shows **Transcribing…**, and then the text appears at the cursor.

A tap shorter than 0.3 seconds is ignored. A recording stops at the maximum length the model accepts (400 seconds for Canary 1B v2). What was recorded up to then is transcribed and inserted.

The app can't always insert the text. It leaves the text on the clipboard and shows a notification when:

- the active window changed during the dictation,
- the target window runs as administrator, or
- modifier keys were still held.

Paste the text with Ctrl+V.

### Tray menu

Right-click the tray icon:

| Item | What it does |
|---|---|
| **Settings…** | Opens the settings window. Double-clicking the tray icon does the same. |
| **Download model…** | Opens the download window. Shown only while the selected model is not installed. |
| **Cancel transcription** | Stops the running transcription. Shown only while a dictation is transcribed. |
| **Exit** | Ends the app. |

### Settings

The settings window applies changes when you save them, without a restart. It has five sections:

- **Dictation:** push-to-talk hotkey (a single key or a key combination), translate or transcribe, spoken language, target language, and silence trimming.
- **Model:** download, delete and select speech models.
- **Engine:** choose the compute backend: Auto (the GPU when it works, otherwise the CPU), Vulkan only, or CPU only. It also shows the engine status.
- **Text insertion:** paste through the clipboard (with or without restoring the clipboard), or type the text. Typing leaves the clipboard alone but is slower for long text.
- **General:** start with Windows. This adds an entry to the current user's `Run` registry key.

The settings are saved in `settings.json`. A file with the defaults looks like this:

```json
{
  "schemaVersion": 1,
  "model": { "selectedModelId": "canary-1b-v2-q8_0" },
  "transcription": { "backend": "auto", "task": "translate", "sourceLanguage": "de", "targetLanguage": "en" },
  "recording": { "hotkey": ["VcRightControl"] },
  "textInsertion": { "method": "clipboardPaste", "restoreClipboard": true },
  "voiceActivity": { "enabled": true }
}
```

Hotkey names are SharpHook `KeyCode` names, such as `VcRightControl` or `VcF9`. Languages are ISO 639-1 codes.

### Speech models

| Model | Download size | Languages |
|---|---|---|
| Canary 1B v2 (Q8_0), the default | 1.07 GB | 25 European languages |
| Canary 1B v2 (Q4_K_M) | 701 MB | 25 European languages |
| Canary 180M Flash (Q8_0) | 208 MB | German, English, French, Spanish |

The models are GGUF conversions of NVIDIA's Canary models by [handy-computer](https://huggingface.co/handy-computer), downloaded from Hugging Face. NVIDIA licenses them under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). Translation goes from English into another language or from another language into English.

## Data and privacy

All data is stored per user in `%LOCALAPPDATA%\Pisum Transcribe\`, and nothing roams:

| Path | Contents |
|---|---|
| `settings.json` | The settings |
| `models\` | Downloaded speech models |
| `logs\` | A daily log file. The last 7 files are kept. |

The log contains durations, lengths and status codes. It never contains audio or transcript text.

## Development

```sh
dotnet build Pisum.Transcribe.slnx
dotnet test Pisum.Transcribe.slnx                                                   # unit and integration tests
dotnet test Pisum.Transcribe.slnx --filter-class "*.JsonSettingsStoreTests"         # one test class
dotnet test Pisum.Transcribe.slnx --filter-trait "Category=Hardware" --explicit on  # needs a microphone, GPU or model
```

The solution uses the `.slnx` format, so pass it to `dotnet` commands explicitly. The tests use xunit v3 on Microsoft.Testing.Platform, with Shouldly and FakeItEasy. Hardware tests are marked explicit, so the default test run needs no microphone, GPU or downloaded model.

The app is a WPF app on the .NET Generic Host, with no main window. Each feature lives in its own folder and namespace under `src/Pisum.Transcribe/`:

| Folder | Contents |
|---|---|
| `Hosting/` | Host setup, single instance, logging and shutdown |
| `Tray/` | Tray icon and menu |
| `Settings/` | Settings model and JSON store |
| `SpeechModels/` | Model catalog, download and verification, setup window |
| `Transcription/` | transcribe.cpp engine with Vulkan and CPU fallback |
| `Recording/` | Push-to-talk hotkey and microphone recording (WASAPI) |
| `VoiceActivity/` | Silence trimming with Silero VAD |
| `TextInsertion/` | Paste or type at the cursor, clipboard restore |
| `Dictation/` | The dictation workflow, overlay and notifications |
| `SettingsWindow/` | Settings window |

[CLAUDE.md](CLAUDE.md) describes the conventions for shutdown, the UI thread, settings, logging, code style and tests.

### Planning

- [docs/idea.md](docs/idea.md) describes the idea and the technology choices.
- [docs/roadmap.md](docs/roadmap.md) lists the planned changes and what is deferred.
- Changes are planned with [OpenSpec](https://github.com/Fission-AI/OpenSpec): the specs are in `openspec/specs/`, and the changes, open and archived, are in `openspec/changes/`.
- Work is tracked in the [GitLab project](https://gitlab.com/pisum-projects/projects/whisper/transcribe).

## Project status

The v1 feature set on the roadmap is implemented. Packaging (installer, auto-update, code signing) and a CI pipeline are not done yet.

## Third-party notices

[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) lists the third-party components the app ships and their licenses.
