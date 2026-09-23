# Pisum Transcribe

Pisum Transcribe is a push-to-talk dictation app for Windows. It runs in the system tray. Hold the hotkey, speak, and release it: the app transcribes your speech and inserts the text at the cursor in the active window. By default it translates German speech into English text.

A version for macOS on Apple silicon is coming. It isn't released yet, so this README covers Windows only.

Speech recognition runs on your computer with an NVIDIA Canary model and the [transcribe.cpp](https://github.com/handy-computer/transcribe.cpp) engine. Audio and text never leave the machine. The app goes online to download a speech model and, unless you turn it off in the settings, once a day to ask GitHub whether a new version exists.

## Features

- **Hold to talk:** a global hotkey (Right Ctrl by default) records while you hold it, in any application.
- **Translate or transcribe:** translate into another language (German to English by default), or transcribe in the spoken language. The Canary 1B v2 models support 25 European languages.
- **Text at the cursor:** the text is pasted through the clipboard, and the previous clipboard contents are put back. Typing the text as keyboard input is an option.
- **Local and fast:** the model stays loaded between dictations. It runs on the GPU through Vulkan and falls back to the CPU when the GPU fails.
- **Silence trimming:** [Silero VAD](https://github.com/snakers4/silero-vad) cuts the silence before and after your speech, and recordings without speech are not transcribed.
- **Feedback:** an overlay shows the recording time and the transcription progress, and the tray icon, a monochrome microphone, shows the app state. When the app is ready, the icon is black on a light taskbar and white on a dark one, and it follows a change of the Windows mode at once. It is dimmed while no model is available, red while recording and amber while transcribing.

## Requirements

- Windows 10 version 2004 or later, or Windows 11, x64
- A microphone
- A GPU with a Vulkan driver is optional. Without one, the model runs on the CPU.
- 0.2 to 1.1 GB of disk space for a speech model, and about 230 MB for the app

## Getting started

1. Download `Pisum.Transcribe_<version>_win-x64.msi` from the [latest release](https://github.com/mschnecke/pisum-transcribe/releases). It is about 77 MB.
2. Open it. The installer isn't code-signed, so Windows SmartScreen may show **Windows protected your PC**. Choose **More info**, then **Run anyway**.

The installer asks no questions and needs no administrator rights. It installs the app for your user into `%LOCALAPPDATA%\Programs\Pisum Transcribe\`, about 230 MB, and adds **Pisum Transcribe** to the Start Menu. Nothing else needs to be installed: the installer contains .NET and the Visual C++ runtime. A GPU with a Vulkan driver is optional.

The app starts when the installation finishes. On the first start, it opens the window **Download a speech model**. Choose a model and download it. The download is checked against its SHA-256 hash, and the model loads when it is complete. The tray tooltip shows **Ready (Vulkan)** or **Ready (CPU)** when you can dictate.

Only one instance runs at a time. A second start waits up to 6 seconds for the first instance to exit and then exits itself.

### Upgrading

Open the MSI of a newer release. If the app is running, Windows Installer says **The following applications should be closed before continuing the install** and lists `Pisum.Transcribe`. Choose **OK**, and it closes the app. The new version starts when the upgrade finishes. Your settings, speech models, logs and **Start with Windows** stay as they are.

A final release installs over its release candidates, such as `0.1.0` over `0.1.0-rc.2`. An older release doesn't install over a newer one: the installer says **A newer version of Pisum Transcribe is already installed**. To go back, uninstall first.

### Uninstalling

Uninstall **Pisum Transcribe** in Windows Settings under **Apps** > **Installed apps**. This removes the program, its Start Menu entry and its **Start with Windows** entry. It keeps `%LOCALAPPDATA%\Pisum Transcribe\` with your settings, speech models and logs, so a new installation picks them up again. Delete that folder to remove them too.

### Coming from the zip

Releases up to `0.1.0-rc.1` came as a zip. Exit the zip's app from its tray menu, install the MSI, and then delete the extracted `Pisum Transcribe` folder. The installed app uses the same settings and models. On its first start, it points an existing **Start with Windows** entry to itself.

### Build from source

You need the [.NET SDK 10.0.400](https://dotnet.microsoft.com/download) or a later feature band that `global.json` allows. Build and start the app:

```sh
git clone https://github.com/mschnecke/pisum-transcribe.git
cd pisum-transcribe
dotnet run --project src/Pisum.Transcribe -f net10.0-windows10.0.19041.0
```

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
| **Settings…** | Opens the settings window. A left click on the tray icon does the same. |
| **Download model…** | Opens the download window. Shown only while the selected model is not installed. |
| **Cancel transcription** | Stops the running transcription. Shown only while a dictation is transcribed. |
| **Pisum Transcribe `<version>` is available…** | Opens the release page in the browser. Shown only when a newer version is released. |
| **Exit** | Ends the app. |

### Settings

The settings window applies changes when you save them, without a restart. It has five sections:

- **Dictation:** push-to-talk hotkey (a single key or a key combination), translate or transcribe, spoken language, target language, and silence trimming.
- **Model:** download, delete and select speech models.
- **Engine:** choose the compute backend: Auto (the GPU when it works, otherwise the CPU), Vulkan only, or CPU only. It also shows the engine status.
- **Text insertion:** paste through the clipboard (with or without restoring the clipboard), or type the text. Typing leaves the clipboard alone but is slower for long text.
- **General:** start with Windows, which adds an entry to the current user's `Run` registry key, and check for updates automatically, which asks GitHub once a day whether a new version exists. The update check is on by default.

The settings are saved in `settings.json`. A file with the defaults looks like this:

```json
{
  "schemaVersion": 1,
  "model": { "selectedModelId": "canary-1b-v2-q8_0" },
  "transcription": { "backend": "auto", "task": "translate", "sourceLanguage": "de", "targetLanguage": "en" },
  "recording": { "hotkey": ["VcRightControl"] },
  "textInsertion": { "method": "clipboardPaste", "restoreClipboard": true },
  "voiceActivity": { "enabled": true },
  "updates": { "checkAutomatically": true }
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

Once a day, unless you turn off **Check for updates automatically** in the settings, the app asks GitHub whether a new version exists. The update check sends no audio, text, settings, logs or identifiers. Its request carries only what every HTTPS request carries: your IP address, and a user agent that names the app without its version. When a newer version is released, the tray menu and a notification say so. The app doesn't download or install it.

## Development

```sh
dotnet build Pisum.Transcribe.slnx
dotnet test Pisum.Transcribe.slnx                                                   # unit and integration tests
dotnet test Pisum.Transcribe.slnx --filter-class "*.JsonSettingsStoreTests"         # one test class
dotnet test Pisum.Transcribe.slnx --filter-trait "Category=Hardware" --explicit on  # needs a microphone, GPU, model or internet access
```

The solution uses the `.slnx` format, so pass it to `dotnet` commands explicitly. The tests use xunit v3 on Microsoft.Testing.Platform, with Shouldly and FakeItEasy. Hardware tests are marked explicit, so the default test run needs no microphone, GPU, downloaded model or internet access.

The app is an Avalonia app on the .NET Generic Host, with no main window. Each feature lives in its own folder and namespace under `src/Pisum.Transcribe/`:

| Folder | Contents |
|---|---|
| `Hosting/` | Host setup, single instance, logging and shutdown |
| `Tray/` | Tray icon and menu, the app icon and the status glyph, whose icons `tools/generate-tray-icon.cs` renders |
| `Notifications/` | Windows notifications from Pisum Transcribe |
| `Settings/` | Settings model and JSON store |
| `SpeechModels/` | Model catalog, download and verification, setup window |
| `Transcription/` | transcribe.cpp engine with Vulkan and CPU fallback |
| `Recording/` | Push-to-talk hotkey and microphone recording (WASAPI) |
| `VoiceActivity/` | Silence trimming with Silero VAD |
| `TextInsertion/` | Paste or type at the cursor, clipboard restore |
| `Dictation/` | The dictation workflow, overlay and notifications |
| `SettingsWindow/` | Settings window |
| `Updates/` | The daily update check and its tray notice |

[CLAUDE.md](CLAUDE.md) describes the conventions for shutdown, the UI thread, settings, logging, code style and tests. [packaging/README.md](packaging/README.md) describes how the MSI is built and how a release is published.

### Planning

- [docs/idea.md](docs/idea.md) describes the idea and the technology choices.
- [docs/roadmap.md](docs/roadmap.md) lists the planned changes and what is deferred.
- Changes are planned with [OpenSpec](https://github.com/Fission-AI/OpenSpec): the specs are in `openspec/specs/`, and the changes, open and archived, are in `openspec/changes/`.
- Work is tracked in [GitHub issues](https://github.com/mschnecke/pisum-transcribe/issues), and changes reach `main` through pull requests.

## Project status

The v1 feature set on the roadmap is implemented. GitHub Actions builds, tests and packages every pull request and every push to `main`, and releases are published as an MSI installer on [GitHub Releases](https://github.com/mschnecke/pisum-transcribe/releases). Automatic updates and code signing are not done yet.

## Third-party notices

[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) lists the third-party components the app ships and their licenses. Every release also carries the source of libuiohook (LGPL) and WiX (MS-RL), the two copyleft components in the MSI.
