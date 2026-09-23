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
src/Pisum.Transcribe/            Avalonia tray app (WinExe, net10.0-windows10.0.19041.0 for the WinRT toast API, win-x64), no main window
  Program.cs                     Entry point: single-instance guard, bootstrap logger, Avalonia's classic desktop lifetime on Win32 and Skia
  App.axaml(.cs)                 The Fluent theme; builds and starts the host, creates ShutdownCoordinator, handles the end of the Windows session
  NativeMethods.txt              Win32 functions that CsWin32 generates into Windows.Win32.PInvoke
  Hosting/                       AppHost, AppPaths, SingleInstanceGuard, ShutdownCoordinator, DispatcherWait, IUiDispatcher (AvaloniaUiDispatcher), logging setup
  Dialogs/                       ConfirmDialog, the Yes/No question of the windows
  Tray/                          ITrayIconService (menu items, TrayStatus icon), TrayIconService (Avalonia's TrayIcon and NativeMenu, status icons per taskbar mode), ITaskbarModeWatcher; TrayIcon.svg (the app icon) with the TrayIcon.ico and TrayIcon.png (256 px) generated from it, and TrayGlyph.svg (the monochrome status glyph)
    Windows/                     The status ICOs generated from TrayGlyph.svg (embedded), TaskbarModeWatcher (SystemUsesLightTheme, RegNotifyChangeKeyValue)
    MacOS/                       The menu bar PNGs generated from TrayGlyph.svg (@1x and @2x, templates for ready and unavailable), not referenced by the project yet
  Notifications/                 INotifier, which shows a notification from any thread; AddNotifications()
    Windows/                     ToastNotifier (WinRT toasts behind IToastSender), ToastRegistration (the AppUserModelID Pisum.Transcribe under HKCU\Software\Classes at every start)
  Settings/                      AppSettings and its section records, ISettingsStore, JsonSettingsStore
  SpeechModels/                  ModelCatalog, IModelStore/ModelStore (download, verify), setup window, tray item
  Transcription/                 ITranscriber, TranscribeCppTranscriber (worker, fallback), native seam and adapter, hosted service
  Recording/                     Push-to-talk hotkey (SharpHook), microphone capture (NAudio WASAPI), AudioRecorder
  VoiceActivity/                 Silero VAD on ONNX Runtime (Assets/silero_vad.onnx), AudioTrimmer
  TextInsertion/                 TextInserter: clipboard paste with restore or typed input, foreground window and elevation checks
  Dictation/                     DictationController (the hold-to-talk loop), recording overlay, tray status and notifications
  SettingsWindow/                Settings dialog and section view models, SettingsApplier (live apply), autostart
  Updates/                       UpdateCheckService (the daily update check against GitHub's latest release, tray notice), ReleaseVersion
tests/Pisum.Transcribe.Tests/    xunit v3 on Microsoft.Testing.Platform, Shouldly, FakeItEasy, FakeTimeProvider; folders mirror src
tools/generate-tray-icon.cs      Renders Tray/TrayIcon.svg and Tray/TrayGlyph.svg into the app icon, the status ICOs and the macOS PNGs (a .NET file-based app on SkiaSharp)
.github/workflows/               ci.yml (build, test and MSI on every PR and push to main), release.yml (bump, tag, test, publish the MSI)
.config/dotnet-tools.json        Local tool manifest that pins WiX (`wix`), restored by build-msi.ps1
packaging/                       bump-version.sh, windows/build-msi.ps1 with the MSI's WiX source Pisum.Transcribe.wxs and the guard assert-native-dependencies.ps1, third-party/ (the notices of ONNX Runtime, .NET, Avalonia and SkiaSharp); see packaging/README.md
```

## Architecture and conventions

- **Feature folders:** each folder is its own namespace (`Pisum.Transcribe.<Feature>`). A feature registers its services with one `services.Add<Feature>()` extension method, called from `AppHost.Create`. Background work runs as an `IHostedService` or `BackgroundService`.
- **Platform folders:** code that is Windows-only and stays Windows-only goes into a `Windows/` subfolder of its feature folder (later `MacOS/` the same way), and keeps the feature's namespace. The folder marks the platform, not a namespace. Each platform folder needs an entry in the project's `.csproj.DotSettings` (`src/Pisum.Transcribe/Pisum.Transcribe.csproj.DotSettings`, `tests/Pisum.Transcribe.Tests/Pisum.Transcribe.Tests.csproj.DotSettings`) that marks it as not a namespace provider, or Rider flags the namespace.
- **Dictation flow:** `DictationController` connects the features. A hotkey press starts `IAudioRecorder` and captures the foreground window. On release, `IVoiceActivityDetector` trims the silence, `ITranscriber` transcribes, and `ITextInserter` inserts the text into the captured window. Hotkey and recorder events are queued in one channel, and one loop owns the state. `IDictationFeedback` drives the overlay, the tray icon and notifications.
- **Tray menu:** a feature adds its menu item with `ITrayIconService.AddMenuItem(header, onClick, isVisible)` (see `ModelSetupHostedService`, `DictationFeedback`, `SettingsWindowService`).
- **Shutdown:** `ShutdownCoordinator` is the only code that ends the app. Don't call the lifetime's `Shutdown` or `Environment.Exit`, or stop the host, anywhere else. A service must stop within `HostOptions.ShutdownTimeout` (4 s). After 4.5 s a watchdog ends the process with `TerminateProcess`, so don't rely on `AppDomain.ProcessExit` for cleanup. The end of the Windows session (sign-out, shutdown or restart) reaches `ShutdownCoordinator` through the lifetime's `ShutdownRequested`, which `App` handles by waiting for the shutdown while the UI thread keeps processing messages (`DispatcherWait`). On Windows every `ShutdownRequested` is the end of the session, because **Exit** calls the coordinator directly. Never cancel `ShutdownRequested`, because that vetoes the sign-out.
- **UI thread:** services that touch the windows or the tray icon marshal through `IUiDispatcher.InvokeAsync`, which queues the action, also on the UI thread, and keeps an exception in the returned task. `AvaloniaUiDispatcher` maps it to `Dispatcher.UIThread.InvokeAsync`, never `Post`, which would send the exception to `ShutdownCoordinator`. `DispatcherWait.Until` runs Avalonia's `DispatcherFrame` until a task completes; Avalonia still runs the operations already queued before it leaves the frame. Never block the UI thread while waiting for async work.
- **Notifications:** show them with `INotifier.Show(title, message)`, which may be called from any thread and moves to the thread it needs itself.
- **Data:** all per-user data lives under `%LOCALAPPDATA%\Pisum Transcribe\` (see `AppPaths`): `settings.json`, `logs\` and `models\`. Nothing roams, and nothing leaves the machine.
- **Settings:** add a feature's section to `AppSettings` and follow the shape rules in its XML doc. `ISettingsStore.Changed` is raised after a save. `SettingsApplier` applies hotkey, model and backend changes at once. Every other setting is read at the start of each dictation. A new setting must be read that way, get a case in `SettingsApplier`, or be read by its own service before each use, as `UpdateCheckService` reads "Check for updates automatically" before each update check.
- **Logging:** use `ILogger<T>`. Serilog writes a daily rolling file and keeps 7 files. Never log transcript text or audio data, only lengths, durations and status codes.
- **Time:** inject `TimeProvider` for timers, delays and durations. Tests use `FakeTimeProvider`.
- **Win32:** add the function to `NativeMethods.txt` and call `Windows.Win32.PInvoke` (CsWin32). Don't write a new `DllImport`. The test helpers call the same generated `PInvoke`, which they see through `InternalsVisibleTo`; a function that only they need goes under the `// Test helpers` comment at the end of `NativeMethods.txt`.
- **View models:** use CommunityToolkit.Mvvm (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`).
- **Views:** Avalonia XAML (`.axaml`) with the Fluent theme and compiled bindings, so every view declares `x:DataType` and a binding mistake fails the build. A window keeps a parameterless constructor for the XAML loader next to the one that takes its view model. Questions use `ConfirmDialog`, which is asynchronous: a window that asks before it closes cancels `Closing` first (`ConfirmDialog.AskBeforeClosing`).
- **Code style:** don't add file headers (such as copyright blocks). Types are `internal sealed` by default, because public members need XML docs and a missing doc fails the build. Fields use `_camelCase`, and async methods end in `Async`. The test project and FakeItEasy (`DynamicProxyGenAssembly2`) can see internal types.
- **Tests:** each test class carries `[Trait(Traits.Category, Traits.Categories.Unit | Integration | Hardware)]`, test methods are named `Method_Scenario_Expected`, and test bodies use Arrange/Act/Assert. Tests that need a microphone, a GPU, a downloaded model or internet access are `Hardware` tests marked `[Fact(Explicit = true)]`, so the default test run stays hermetic.
  - Tests of windows, the tray and the dispatcher run on Avalonia's headless platform: a plain `[Fact]` runs its body through `HeadlessUi.RunAsync` (`Avalonia.Headless.XUnit` doesn't run on xunit.v3 4.x). The dispatcher runs only while the body awaits, and text is measured with Skia and the system's fonts, as in the app. A test that needs a real window handle, such as the overlay's native styles, is a `Hardware` test on Avalonia's Win32 platform (see `RecordingOverlayWindowHardwareTests`); a process runs only one Avalonia platform, so run it without the headless tests. `TextInsertion/TestWindow` is a plain Win32 `EDIT` window on its own thread.
  - Tests that use the real desktop (the clipboard, the foreground window or simulated keys) go in `[Collection(DesktopCollection.Name)]`, which runs them one at a time.
  - Hardware tests call `Assert.SkipWhen` when an asset is missing. The transcription tests use the models installed in `%LOCALAPPDATA%\Pisum Transcribe\models\` and read WAV files (16 kHz mono) from `PISUM_TRANSCRIBE_TEST_AUDIO` (German) and `PISUM_TRANSCRIBE_TEST_AUDIO_EN` (English).
  - Shared helpers are at the test project root: `TempDirectory`, `CapturingLogger`, `FakeHttpMessageHandler`, `HeadlessUi`, and `InlineUiDispatcher`, which runs the action at once for service tests.

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
dotnet run tools/generate-tray-icon.cs                                                # rebuild the tray icons after editing TrayIcon.svg or TrayGlyph.svg
dotnet sln Pisum.Transcribe.slnx add <path/to/Project.csproj>                        # register a new project
./packaging/windows/build-msi.ps1 -Version 0.1.0-dev.1                               # the release MSI, built and validated, into artifacts\ (PowerShell 7)
./packaging/bump-version.sh patch                                                    # write the next version into Directory.Build.props (Git Bash)
gh workflow run release.yml -f bump=patch                                            # start a release: bump, commit, tag and publish
```

`Directory.Build.props` records the last released version, and a release takes its version from the tag. Both ways to release, and how the MSI installs, upgrades and uninstalls, are in `packaging/README.md`.

Add every new project to `Pisum.Transcribe.slnx`, or the solution-level build and test commands will skip it.

Only one instance runs at a time. A second launch waits up to 6 s and then exits. Logs are in `%LOCALAPPDATA%\Pisum Transcribe\logs\`.

## Repository

- GitHub repository: `mschnecke/pisum-transcribe` (remote `git@github.pisum:mschnecke/pisum-transcribe.git`). Use `gh` for issues, pull requests, workflow runs and releases.
- The default branch is `main`. Changes reach it through pull requests to `main`.
