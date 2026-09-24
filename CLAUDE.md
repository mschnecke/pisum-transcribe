# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Pisum Transcribe is a push-to-talk dictation app for Windows that runs in the system tray. The macOS port is under way: the Mac build runs in the menu bar and dictates as on Windows, and open at login and the installer come with the later macOS changes in `docs/roadmap.md`. `docs/idea.md` describes the idea, and `docs/roadmap.md` lists the planned changes. Planning uses OpenSpec: open changes are in `openspec/changes/`, finished ones in `openspec/changes/archive/`, and specs are in `openspec/specs/`.

- `global.json` pins the .NET SDK to `10.0.400` and sets Microsoft.Testing.Platform as the test runner.
- `Pisum.Transcribe.slnx` is the solution, in the XML `.slnx` format (not a classic `.sln`).
- `Directory.Build.props` applies to every project: Nullable, ImplicitUsings, LangVersion latest, TreatWarningsAsErrors and GenerateDocumentationFile. It also names the two target frameworks, `PisumWindowsTargetFramework` (`net10.0-windows10.0.19041.0`) and `PisumMacTargetFramework` (`net10.0`).
- `Directory.Packages.props` manages package versions centrally. Add a `<PackageVersion>` there and a `<PackageReference>` without a version in the project. Pin a version exactly (`[x.y.z]`) only with a comment that says why, as for the native ABI of TranscribeCppSharp.

## Layout

```
src/Pisum.Transcribe/            Avalonia tray app (WinExe), no main window, for two frameworks: net10.0-windows10.0.19041.0 (win-x64, for the WinRT toast API) and net10.0 (osx-arm64, macOS 14 or later)
  Program.cs                     Entry point: single-instance guard, bootstrap logger, Avalonia's classic desktop lifetime on Win32 or Avalonia.Native, and Skia
  App.axaml(.cs)                 The Fluent theme; builds and starts the host, creates ShutdownCoordinator, handles the end of the Windows or macOS session and SIGTERM on macOS
  NativeMethods.txt              Win32 functions that CsWin32 generates into Windows.Win32.PInvoke (Windows framework only)
  MacOS/Info.plist               The template of the dev app bundle's Info.plist (@VERSION@ and @MINIMUM_SYSTEM_VERSION@ filled in by the build)
  Hosting/                       AppHost, AppPaths, SingleInstanceGuard, ShutdownCoordinator, DispatcherWait, IUiDispatcher (AvaloniaUiDispatcher), IProcessActivity (App Nap), logging setup
    Windows/                     ProcessTermination (TerminateProcess)
    MacOS/                       ProcessTermination (_exit), MacProcessActivity (a ProcessInfo activity through the helper), MacNativeLibrary (the Swift helper's ABI check) and PisumMac (its functions), QuitEventSender (logout or quit), CoreFoundation (free space for a download, Time Machine exclusion, the Accessibility check and prompt), AppBundle (the enclosing .app, `open -n` for the relaunch)
  Dialogs/                       ConfirmDialog, the Yes/No question of the windows
  Tray/                          ITrayIconService (menu items, TrayStatus icon), TrayIconService (Avalonia's TrayIcon and NativeMenu), ITrayIconSet (the platform's icon per status); TrayIcon.svg (the app icon) with the TrayIcon.ico and TrayIcon.png (256 px) generated from it, and TrayGlyph.svg (the monochrome status glyph)
    Windows/                     WindowsTrayIconSet with the status ICOs generated from TrayGlyph.svg (embedded), TaskbarModeWatcher (SystemUsesLightTheme, RegNotifyChangeKeyValue)
    MacOS/                       MacTrayIconSet with the menu bar PNGs generated from TrayGlyph.svg (@1x and @2x, templates for ready and unavailable; the @2x ones are embedded), AppIcon.icns (the bundle's icon)
  Notifications/                 INotifier, which shows a notification from any thread; AddNotifications()
    Windows/                     ToastNotifier (WinRT toasts behind IToastSender), ToastRegistration (the AppUserModelID Pisum.Transcribe under HKCU\Software\Classes at every start)
    MacOS/                       MacNotifier (UNUserNotificationCenter through the Swift helper, behind INotificationCenter; reads the permission at startup, which the setup window asks for)
  Settings/                      AppSettings and its section records, ISettingsStore, JsonSettingsStore
  SpeechModels/                  ModelCatalog, IModelStore/ModelStore (download, verify, IsDownloading), setup window (with the permission rows on macOS), its one tray item, ISetupWindow; MacOS/ MacFreeSpace (purgeable space counts)
  Permissions/                   macOS only: IPermissions, PermissionsViewModel (the setup window's four permission rows); MacOS/ MacPermissions, RelaunchService (restart after the Accessibility grant), AddPermissions()
  Transcription/                 ITranscriber, TranscribeCppTranscriber (worker, fallback), native seam and adapter, hosted service
  Recording/                     Push-to-talk hotkey (SharpHook) with its key-state read (IHotkeyKeyState) and grant check (IHookAccess) per platform, microphone capture (NAudio WASAPI), AudioRecorder, HotkeyParser (the default hotkey per platform)
    MacOS/                       MacHotkeyKeyState (the HID key state, modifiers from their flag bits, lock and user switch), MacHookAccess (the Accessibility grant in effect), CoreAudio interop, AudioQueueCaptureSession(Factory), which follows the default input device
  VoiceActivity/                 Silero VAD on ONNX Runtime (Assets/silero_vad.onnx), AudioTrimmer
  TextInsertion/                 TextInserter: clipboard paste with restore or typed input, foreground window and elevation checks, secure input (ISecureInput); the Win32 parts and SharpHookKeyboardInput in Windows/
    MacOS/                       MacClipboardService (the pasteboard through the Swift helper on its own thread, read only while macOS allows it without asking), MacKeyboardInput (CGEvent keystrokes, Command+V with the layout's key of V), MacForegroundWindowTracker (the focused window and its frame through the Accessibility API), MacSecureInput
  Dictation/                     DictationController (the hold-to-talk loop), IDictationState (a dictation in progress), recording overlay (placement and styles behind IOverlayPlatform, Win32OverlayPlatform in Windows/), tray status (with IHotkeyAvailability, AlwaysAvailableHotkey in Windows/) and notifications
    MacOS/                       MacOverlayPlatform (the screen of the target's frame, the native settings through the helper), MacHotkeyAvailability (the Accessibility grant in effect, the secure-input poll)
  SettingsWindow/                Settings dialog and section view models, HotkeyKeyNames (the hotkey editor's key names and rule per platform), SettingsApplier (live apply), autostart
  Updates/                       UpdateCheckService (the daily update check against GitHub's latest release, tray notice), ReleaseVersion
src/Pisum.Transcribe.MacNative/  Swift sources of the helper libPisumMac.dylib, which the app project builds with swiftc for the macOS framework on a macOS host
tests/Pisum.Transcribe.Tests/    xunit v3 on Microsoft.Testing.Platform, Shouldly, FakeItEasy, FakeTimeProvider; folders mirror src; builds only the host's framework
tools/generate-tray-icon.cs      Renders Tray/TrayIcon.svg and Tray/TrayGlyph.svg into the app icon, AppIcon.icns, the status ICOs and the macOS PNGs (a .NET file-based app on SkiaSharp)
.github/workflows/               ci.yml (build and test on Windows and macOS, the MSI on Windows, on every PR and push to main), release.yml (bump, tag, test, publish the MSI)
.config/dotnet-tools.json        Local tool manifest that pins WiX (`wix`), restored by build-msi.ps1
packaging/                       bump-version.sh, windows/build-msi.ps1 with the MSI's WiX source Pisum.Transcribe.wxs and the guard assert-native-dependencies.ps1, third-party/ (the notices of ONNX Runtime, .NET, Avalonia and SkiaSharp); see packaging/README.md
```

## Architecture and conventions

- **Feature folders:** each folder is its own namespace (`Pisum.Transcribe.<Feature>`). A feature registers its services with one `services.Add<Feature>()` extension method, called from `AppHost.Create`. Background work runs as an `IHostedService` or `BackgroundService`.
- **Platform folders:** code that is Windows-only goes into a `Windows/` subfolder of its feature folder, and macOS-only code into `MacOS/`. Both keep the feature's namespace: the folder marks the platform, not a namespace. `Windows/` compiles only for the Windows framework and `MacOS/` only for the macOS one, so a platform call in shared code fails the other platform's build. `Add<Feature>()` picks the implementation with `#if WINDOWS`, which only the Windows framework defines. A type that both platforms implement can keep one name in both folders, such as `ProcessTermination`. `AppHost.Create` registers every feature on both platforms, and the permissions only on macOS. Each platform folder needs an entry in the project's `.csproj.DotSettings` (`src/Pisum.Transcribe/Pisum.Transcribe.csproj.DotSettings`, `tests/Pisum.Transcribe.Tests/Pisum.Transcribe.Tests.csproj.DotSettings`) that marks it as not a namespace provider, or Rider flags the namespace.
- **Dictation flow:** `DictationController` connects the features. A hotkey press starts `IAudioRecorder` and captures the foreground window. On release, `IVoiceActivityDetector` trims the silence, `ITranscriber` transcribes, and `ITextInserter` inserts the text into the captured window. Hotkey and recorder events are queued in one channel, and one loop owns the state. `IDictationFeedback` drives the overlay, the tray icon and notifications. Between dictations the tray shows the engine's status and, with a ready engine, why the hotkey can't work (`IHotkeyAvailability`: on macOS the Accessibility grant not in effect, or secure input on). `IDictationState` tells other services that a dictation is in progress, and each dictation, model load, transcription and the voice activity warm-up run inside an `IProcessActivity`, so App Nap doesn't throttle them.
- **Tray menu:** a feature adds its menu item with `ITrayIconService.AddMenuItem(header, onClick, isVisible)` (see `ModelSetupHostedService`, `DictationFeedback`, `SettingsWindowService`).
- **Shutdown:** `ShutdownCoordinator` is the only code that ends the app. Don't call the lifetime's `Shutdown` or `Environment.Exit`, or stop the host, anywhere else. A service must stop within `HostOptions.ShutdownTimeout` (4 s). After 4.5 s a watchdog ends the process with `TerminateProcess`, so don't rely on `AppDomain.ProcessExit` for cleanup. The end of the Windows session (sign-out, shutdown or restart) reaches `ShutdownCoordinator` through the lifetime's `ShutdownRequested`, which `App` handles by waiting for the shutdown while the UI thread keeps processing messages (`DispatcherWait`). On Windows every `ShutdownRequested` is the end of the session, because **Exit** calls the coordinator directly. On macOS it's a quit event, and `QuitEventSender` reads its sender: `loginwindow` means the end of the session, anything else a user exit. `SIGTERM` ends the app like **Quit Pisum Transcribe**, as `ShutdownReason.TerminationRequest`, and the process ends with `_exit` instead of `TerminateProcess`. `ShutdownReason.Relaunch` (macOS, from `RelaunchService` after the Accessibility grant) starts the new instance with `open -n` as the first step, and then ends like **Quit**. Never cancel `ShutdownRequested`, because that vetoes the sign-out.
- **UI thread:** services that touch the windows or the tray icon marshal through `IUiDispatcher.InvokeAsync`, which queues the action, also on the UI thread, and keeps an exception in the returned task. `AvaloniaUiDispatcher` maps it to `Dispatcher.UIThread.InvokeAsync`, never `Post`, which would send the exception to `ShutdownCoordinator`. `DispatcherWait.Until` runs Avalonia's `DispatcherFrame` until a task completes; Avalonia still runs the operations already queued before it leaves the frame. Never block the UI thread while waiting for async work.
- **Notifications:** show them with `INotifier.Show(title, message)`, which may be called from any thread and moves to the thread it needs itself.
- **Data:** all per-user data lives under `%LOCALAPPDATA%\Pisum Transcribe\` (see `AppPaths`): `settings.json`, `logs\` and `models\`. On macOS it lives under `~/Library/Application Support/Pisum Transcribe/`, except the logs, which go to `~/Library/Logs/Pisum Transcribe/`. Nothing roams, and nothing leaves the machine.
- **Settings:** add a feature's section to `AppSettings` and follow the shape rules in its XML doc. `settings.json` records its format version, `AppSettings.CurrentSchemaVersion` (2 since `add-metal-backend`, which renamed the backend value `vulkan` to `gpu`). `JsonSettingsStore` migrates an older file in memory before reading it and writes the current format on the next save; a renamed property or enum value raises the version and adds a step to that migration. `ISettingsStore.Changed` is raised after a save. `SettingsApplier` applies hotkey, model and backend changes at once. The default hotkey is right Ctrl on Windows and right Command on macOS (`HotkeyParser.DefaultKeyName`); tests assert it through that constant. Every other setting is read at the start of each dictation. A new setting must be read that way, get a case in `SettingsApplier`, or be read by its own service before each use, as `UpdateCheckService` reads "Check for updates automatically" before each update check.
- **Logging:** use `ILogger<T>`. Serilog writes a daily rolling file and keeps 7 files. Never log transcript text or audio data, only lengths, durations and status codes.
- **Time:** inject `TimeProvider` for timers, delays and durations. Tests use `FakeTimeProvider`.
- **Win32:** add the function to `NativeMethods.txt` and call `Windows.Win32.PInvoke` (CsWin32). Don't write a new `DllImport` for Win32. The test helpers call the same generated `PInvoke`, which they see through `InternalsVisibleTo`; a function that only they need goes under the `// Test helpers` comment at the end of `NativeMethods.txt`.
- **macOS APIs:** call C APIs (libc, CoreFoundation, the Accessibility C API, CoreGraphics' key events and key and session state, Text Input Sources, CoreAudio and AudioQueue) with `DllImport` from a `MacOS/` folder. A C callback, such as AudioQueue's input callback or a CoreAudio property listener, is a static `[UnmanagedCallersOnly]` method that reaches its object through a `GCHandle`; remove the listener or dispose the queue before freeing the handle. The macOS framework allows unsafe code for these function pointers, as CsWin32 does on Windows. Objective-C APIs go through the Swift helper in `src/Pisum.Transcribe.MacNative/`: one `.swift` file per area, `@_cdecl` functions with C types only (UTF-8 strings in, `Int32` status codes out, callbacks as C function pointers with a context), called from `Hosting/MacOS/PisumMac` on the UI thread and only while `MacNativeLibrary.IsAvailable`. There are three exceptions: `pisum_pasteboard_probe`, which must never run on the UI thread, because macOS's alert blocks the reading thread, the text insertion's pasteboard functions, which run on `MacClipboardService`'s pasteboard thread, and the activity functions (`pisum_activity_begin`, `pisum_activity_end`), which are safe on any thread. Text Input Sources (the keyboard layout) are the opposite: call them only on the UI thread, because recent macOS versions assert it and end the process. When a function's signature or meaning changes, raise `pisum_abi_version` in `Abi.swift` and `MacNativeLibrary.ExpectedAbiVersion` together (4 since `add-macos-dictation`).
- **View models:** use CommunityToolkit.Mvvm (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`).
- **Views:** Avalonia XAML (`.axaml`) with the Fluent theme and compiled bindings, so every view declares `x:DataType` and a binding mistake fails the build. A window keeps a parameterless constructor for the XAML loader next to the one that takes its view model. Questions use `ConfirmDialog`, which is asynchronous: a window that asks before it closes cancels `Closing` first (`ConfirmDialog.AskBeforeClosing`).
- **Code style:** don't add file headers (such as copyright blocks). Types are `internal sealed` by default, because public members need XML docs and a missing doc fails the build. Fields use `_camelCase`, and async methods end in `Async`. The test project and FakeItEasy (`DynamicProxyGenAssembly2`) can see internal types.
- **Tests:** the test project builds only the host's framework, and leaves out the other platform's `Windows/` or `MacOS/` test folders. Each test class carries `[Trait(Traits.Category, Traits.Categories.Unit | Integration | Hardware)]`, test methods are named `Method_Scenario_Expected`, and test bodies use Arrange/Act/Assert. Tests that need a microphone, a GPU, a downloaded model or internet access are `Hardware` tests marked `[Fact(Explicit = true)]`, so the default test run stays hermetic.
  - Tests of windows, the tray and the dispatcher run on Avalonia's headless platform: a plain `[Fact]` runs its body through `HeadlessUi.RunAsync` (`Avalonia.Headless.XUnit` doesn't run on xunit.v3 4.x). The dispatcher runs only while the body awaits, and text is measured with Skia and the system's fonts, as in the app. A test that needs a real window handle, such as the overlay's native styles, is a `Hardware` test on Avalonia's Win32 platform (see `RecordingOverlayWindowHardwareTests`); a process runs only one Avalonia platform, so run it without the headless tests. On macOS a test can't show a window on Avalonia.Native, because AppKit creates windows only on the process's main thread, which the test platform owns; the overlay's native placement and settings there are checked by hand. `TextInsertion/TestWindow` is a plain Win32 `EDIT` window on its own thread.
  - Tests that use the real desktop (the clipboard, the foreground window or simulated keys) go in `[Collection(DesktopCollection.Name)]`, which runs them one at a time.
  - macOS `Integration` tests call the real Swift helper from the test host, which doesn't run as an app bundle. The macOS recorder's `Hardware` tests therefore need the microphone grant of the terminal or IDE that runs them, and skip without it. The pasteboard tests use named pasteboards of their own (`NamedPasteboard`), and the text insertion's `Hardware` tests type into TextEdit (`TextEditDocument`) and need the Accessibility grant of the terminal or IDE.
  - Hardware tests call `Assert.SkipWhen` when an asset is missing. The transcription tests use the models installed in `%LOCALAPPDATA%\Pisum Transcribe\models\` and read WAV files (16 kHz mono) from `PISUM_TRANSCRIBE_TEST_AUDIO` (German) and `PISUM_TRANSCRIBE_TEST_AUDIO_EN` (English).
  - Shared helpers are at the test project root: `TempDirectory`, `CapturingLogger`, `FakeHttpMessageHandler`, `HeadlessUi`, and `InlineUiDispatcher`, which runs the action at once for service tests.

## Commands

The solution file is `.slnx`, so pass it explicitly to `dotnet` commands. The app project builds both frameworks on every host, and the test project only the host's one. `dotnet run` needs the framework, because the app has two:

```sh
dotnet build Pisum.Transcribe.slnx
dotnet test Pisum.Transcribe.slnx
dotnet test Pisum.Transcribe.slnx --filter-class "*.JsonSettingsStoreTests"           # one test class
dotnet test Pisum.Transcribe.slnx --filter-method "*.Load_FileMissing_UsesDefaults"   # one test
dotnet test Pisum.Transcribe.slnx --filter-trait "Category=Unit"                      # one category
dotnet test Pisum.Transcribe.slnx --filter-trait "Category=Hardware" --explicit on    # hardware tests: microphone, GPU, model, desktop
dotnet run --project src/Pisum.Transcribe -f net10.0-windows10.0.19041.0              # start the tray app on Windows
dotnet run --project src/Pisum.Transcribe -f net10.0                                  # open the dev app bundle on macOS, and wait until it ends
dotnet run tools/generate-tray-icon.cs                                                # rebuild the tray icons after editing TrayIcon.svg or TrayGlyph.svg
dotnet sln Pisum.Transcribe.slnx add <path/to/Project.csproj>                        # register a new project
./packaging/windows/build-msi.ps1 -Version 0.1.0-dev.1                               # the release MSI, built and validated, into artifacts\ (PowerShell 7)
./packaging/bump-version.sh patch                                                    # write the next version into Directory.Build.props (Git Bash)
gh workflow run release.yml -f bump=patch                                            # start a release: bump, commit, tag and publish
```

`Directory.Build.props` records the last released version, and a release takes its version from the tag. Both ways to release, and how the MSI installs, upgrades and uninstalls, are in `packaging/README.md`.

Add every new project to `Pisum.Transcribe.slnx`, or the solution-level build and test commands will skip it.

Only one instance runs at a time, per Windows session and per macOS user. A second launch waits up to 6 s and then exits. Logs are in `%LOCALAPPDATA%\Pisum Transcribe\logs\`, and in `~/Library/Logs/Pisum Transcribe/` on macOS.

## Developing on a Mac

- **Requirements:** the .NET SDK from `global.json` and the Xcode Command Line Tools (`xcode-select --install`), which bring `swiftc` and `codesign`. Full Xcode isn't needed. Only Apple silicon is supported.
- **The build** compiles `libPisumMac.dylib` with `swiftc` into the output, and assembles `bin/Debug/net10.0/osx-arm64/Pisum Transcribe.app`: the build output in `Contents/MacOS/`, `AppIcon.icns` and the `Info.plist` from `MacOS/Info.plist`. It signs the bundle inside-out: every nested file, then the bundle. Both steps run only when their inputs changed.
- **Run the bundle,** not the bare executable, so the app has its bundle identifier `io.github.mschnecke.pisum-transcribe`, its notifications and its own permissions: `dotnet run --project src/Pisum.Transcribe -f net10.0` opens it with `open -W`. Without the bundle, notifications only go to the log, and the setup window shows no permission rows. After an Accessibility grant the app restarts itself; `open -W` then returns when the first process ends, and the restarted app keeps running on its own.
- **A fresh first run:** quit the app, run `tccutil reset All io.github.mschnecke.pisum-transcribe` and delete `~/Library/Application Support/Pisum Transcribe/models/`. The next start opens the setup window with the model part and the four permission rows.
- **A stable signing identity.** By default the bundle is signed ad hoc, so macOS sees every build as a new app and asks for its permissions again. Sign with a local self-signed certificate instead, whose grants survive rebuilds. Create it once in the login keychain:

  ```sh
  /usr/bin/openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -keyout key.pem -out cert.pem \
    -subj "/CN=Pisum Transcribe Development" -addext "extendedKeyUsage=codeSigning" \
    -addext "keyUsage=critical,digitalSignature" -addext "basicConstraints=critical,CA:false"
  /usr/bin/openssl pkcs12 -export -inkey key.pem -in cert.pem -out dev.p12 -passout pass:dev
  security import dev.p12 -k ~/Library/Keychains/login.keychain-db -P dev -T /usr/bin/codesign
  rm key.pem cert.pem dev.p12
  ```

  macOS's own `/usr/bin/openssl` (LibreSSL) writes a `.p12` that `security import` reads; OpenSSL 3's default format fails there with "MAC verification failed". Then set `export PisumCodesignIdentity="Pisum Transcribe Development"` in your shell profile, so every build signs with it. The certificate doesn't need to be trusted. The first signing may ask for your login password; choose **Always Allow**. `codesign -d -r- "src/Pisum.Transcribe/bin/Debug/net10.0/osx-arm64/Pisum Transcribe.app"` then shows `certificate leaf` in the designated requirement.

## Repository

- GitHub repository: `mschnecke/pisum-transcribe` (remote `git@github.pisum:mschnecke/pisum-transcribe.git`). Use `gh` for issues, pull requests, workflow runs and releases.
- The default branch is `main`. Changes reach it through pull requests to `main`.
