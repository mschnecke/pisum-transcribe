# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Pisum Transcribe is a Windows push-to-talk dictation app that runs in the system tray. `docs/idea.md` describes the idea, and `docs/roadmap.md` lists the planned changes. Planning uses OpenSpec: open changes are in `openspec/changes/`, finished ones in `openspec/changes/archive/`, and specs are in `openspec/specs/`.

- `global.json` pins the .NET SDK to `10.0.400` and sets Microsoft.Testing.Platform as the test runner.
- `Pisum.Transcribe.slnx` is the solution, in the XML `.slnx` format (not a classic `.sln`).
- `Directory.Build.props` applies to every project: Nullable, ImplicitUsings, LangVersion latest, TreatWarningsAsErrors and GenerateDocumentationFile.
- `Directory.Packages.props` manages package versions centrally. Add a `<PackageVersion>` there and a `<PackageReference>` without a version in the project. Pin a version exactly (`[x.y.z]`) only with a comment that says why, as for the native ABI of TranscribeCppSharp.

## Layout

```
src/Pisum.Transcribe/            WPF tray app (WinExe, net10.0-windows, win-x64), no main window
  Program.cs                     Entry point: single-instance guard, bootstrap logger, App.Run
  App.xaml(.cs)                  Builds and starts the host, creates ShutdownCoordinator, handles the end of the Windows session
  NativeMethods.txt              Win32 functions that CsWin32 generates into Windows.Win32.PInvoke
  Hosting/                       AppHost, AppPaths, SingleInstanceGuard, ShutdownCoordinator, DispatcherWait, logging setup
  Tray/                          ITrayIconService (menu items, status icon, notifications), TrayIconService (H.NotifyIcon), TrayIcon.svg (the app icon) and the TrayIcon.ico generated from it
  Settings/                      AppSettings and its section records, ISettingsStore, JsonSettingsStore
  SpeechModels/                  ModelCatalog, IModelStore/ModelStore (download, verify), setup window, tray item
  Transcription/                 ITranscriber, TranscribeCppTranscriber (worker, fallback), native seam and adapter, hosted service
  Recording/                     Push-to-talk hotkey (SharpHook), microphone capture (NAudio WASAPI), AudioRecorder
  VoiceActivity/                 Silero VAD on ONNX Runtime (Assets/silero_vad.onnx), AudioTrimmer
  TextInsertion/                 TextInserter: clipboard paste with restore or typed input, foreground window and elevation checks
  Dictation/                     DictationController (the hold-to-talk loop), recording overlay, tray icons and notifications
  SettingsWindow/                Settings dialog and section view models, SettingsApplier (live apply), autostart
tests/Pisum.Transcribe.Tests/    xunit v3 on Microsoft.Testing.Platform, Shouldly, FakeItEasy, FakeTimeProvider; folders mirror src
tools/generate-tray-icon.cs      Renders Tray/TrayIcon.svg into TrayIcon.ico (a .NET file-based app)
.github/workflows/               ci.yml (build, test and zip on every PR and push to main), release.yml (bump, tag, test, publish the zip)
packaging/                       bump-version.sh, windows/build-zip.ps1 and its guard assert-native-dependencies.ps1, third-party/ (ONNX Runtime notices); see packaging/README.md
```

## Architecture and conventions

- **Feature folders:** each folder is its own namespace (`Pisum.Transcribe.<Feature>`). A feature registers its services with one `services.Add<Feature>()` extension method, called from `AppHost.Create`. Background work runs as an `IHostedService` or `BackgroundService`.
- **Dictation flow:** `DictationController` connects the features. A hotkey press starts `IAudioRecorder` and captures the foreground window. On release, `IVoiceActivityDetector` trims the silence, `ITranscriber` transcribes, and `ITextInserter` inserts the text into the captured window. Hotkey and recorder events are queued in one channel, and one loop owns the state. `IDictationFeedback` drives the overlay, the tray icon and notifications.
- **Tray menu:** a feature adds its menu item with `ITrayIconService.AddMenuItem(header, onClick, isVisible)` (see `ModelSetupHostedService`, `DictationFeedback`, `SettingsWindowService`).
- **Shutdown:** `ShutdownCoordinator` is the only code that ends the app. Don't call `Application.Shutdown` or `Environment.Exit`, or stop the host, anywhere else. A service must stop within `HostOptions.ShutdownTimeout` (4 s). After 4.5 s a watchdog ends the process with `TerminateProcess`, so don't rely on `AppDomain.ProcessExit` for cleanup. The end of the Windows session (sign-out, shutdown or restart) reaches `ShutdownCoordinator` through `App.OnSessionEnding`, which waits for the shutdown while the UI thread keeps processing messages (`DispatcherWait`). Never cancel `SessionEnding`, because that vetoes the sign-out.
- **UI thread:** services that touch WPF or the tray icon marshal to `Application.Current.Dispatcher`. Never block the UI thread while waiting for async work.
- **Data:** all per-user data lives under `%LOCALAPPDATA%\Pisum Transcribe\` (see `AppPaths`): `settings.json`, `logs\` and `models\`. Nothing roams, and nothing leaves the machine.
- **Settings:** add a feature's section to `AppSettings` and follow the shape rules in its XML doc. `ISettingsStore.Changed` is raised after a save. `SettingsApplier` applies hotkey, model and backend changes at once. Every other setting is read at the start of each dictation, so a new setting must either be read that way or get a case in `SettingsApplier`.
- **Logging:** use `ILogger<T>`. Serilog writes a daily rolling file and keeps 7 files. Never log transcript text or audio data, only lengths, durations and status codes.
- **Time:** inject `TimeProvider` for timers, delays and durations. Tests use `FakeTimeProvider`.
- **Win32:** add the function to `NativeMethods.txt` and call `Windows.Win32.PInvoke` (CsWin32). Don't write a new `DllImport`.
- **View models:** use CommunityToolkit.Mvvm (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`).
- **Code style:** don't add file headers (such as copyright blocks). Types are `internal sealed` by default, because public members need XML docs and a missing doc fails the build. Fields use `_camelCase`, and async methods end in `Async`. The test project and FakeItEasy (`DynamicProxyGenAssembly2`) can see internal types.
- **Tests:** each test class carries `[Trait(Traits.Category, Traits.Categories.Unit | Integration | Hardware)]`, test methods are named `Method_Scenario_Expected`, and test bodies use Arrange/Act/Assert. Tests that need a microphone, a GPU or a downloaded model are `Hardware` tests marked `[Fact(Explicit = true)]`, so the default test run stays hermetic.
  - WPF windows, the clipboard and the tray icon need an STA thread, so those tests run their body on a new STA thread (see `SettingsDialogTests`, `TextInsertion/TestWindow`).
  - Tests that use the real desktop (the clipboard, the foreground window or simulated keys) go in `[Collection(DesktopCollection.Name)]`, which runs them one at a time.
  - Hardware tests call `Assert.SkipWhen` when an asset is missing. The transcription tests use the models installed in `%LOCALAPPDATA%\Pisum Transcribe\models\` and read WAV files (16 kHz mono) from `PISUM_TRANSCRIBE_TEST_AUDIO` (German) and `PISUM_TRANSCRIBE_TEST_AUDIO_EN` (English).
  - Shared helpers are at the test project root: `TempDirectory`, `CapturingLogger`, `FakeHttpMessageHandler`.

## Commands

The solution file is `.slnx`, so pass it explicitly to `dotnet` commands:

```sh
dotnet build Pisum.Transcribe.slnx
dotnet test Pisum.Transcribe.slnx
dotnet test Pisum.Transcribe.slnx --filter-class "*.JsonSettingsStoreTests"           # one test class
dotnet test Pisum.Transcribe.slnx --filter-method "*.Load_FileMissing_UsesDefaults"   # one test
dotnet test Pisum.Transcribe.slnx --filter-trait "Category=Unit"                      # one category
dotnet test Pisum.Transcribe.slnx --filter-trait "Category=Hardware" --explicit on    # hardware tests: microphone, GPU, model, desktop
dotnet run --project src/Pisum.Transcribe                                             # start the tray app
dotnet run tools/generate-tray-icon.cs                                                # rebuild TrayIcon.ico after editing TrayIcon.svg
dotnet sln Pisum.Transcribe.slnx add <path/to/Project.csproj>                        # register a new project
./packaging/windows/build-zip.ps1 -Version 0.1.0-dev.1                               # the release zip, into artifacts\ (PowerShell 7)
./packaging/bump-version.sh patch                                                    # write the next version into Directory.Build.props (Git Bash)
gh workflow run release.yml -f bump=patch                                            # start a release: bump, commit, tag and publish
```

`Directory.Build.props` records the last released version, and a release takes its version from the tag. Both ways to release, and the zip's contents, are in `packaging/README.md`.

Add every new project to `Pisum.Transcribe.slnx`, or the solution-level build and test commands will skip it.

Only one instance runs at a time. A second launch waits up to 6 s and then exits. Logs are in `%LOCALAPPDATA%\Pisum Transcribe\logs\`.

## Repository

- GitHub repository: `mschnecke/pisum-transcript` (remote `git@github.pisum:mschnecke/pisum-transcript.git`). Use `gh` for issues, pull requests, workflow runs and releases.
- The default branch is `main`. Changes reach it through pull requests to `main`.
