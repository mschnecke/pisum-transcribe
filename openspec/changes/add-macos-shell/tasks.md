The order follows the design's Migration Plan. Group 1 makes the project compile for both frameworks with Windows unchanged. The Mac app first starts in group 4, when the dev `.app` exists. Until then, the Windows build and tests on both hosts are the check.

## 1. Two frameworks, with Windows still green

- [x] 1.1 Move the Win32 calls out of the shared files (D14), on Windows only and with no change in behavior:
  - `SharpHookKeyboardInput` to `TextInsertion/Windows/`
  - the placement on the target window's monitor and the extended styles of `RecordingOverlayWindow` to `Dictation/Windows/`, behind a seam the window calls
  - the default of `TextInserter`'s `isSelfElevated` into the Windows registration in `AddTextInsertion`
  - `ExitProcess` in `App` into a Windows platform file
  - Verify: `dotnet build Pisum.Transcribe.slnx` and `dotnet test Pisum.Transcribe.slnx` pass on Windows, and no file outside a `Windows/` folder of the app uses `Windows.Win32`.
- [x] 1.2 Put the tray's icon choice behind a per-platform icon set (D14). Each `TrayStatus` maps to a `WindowIcon` and a template flag, and the set raises `Changed` when the icon has to be reloaded. The Windows set wraps `TrayIcons` and `TaskbarModeWatcher`. `ITaskbarModeWatcher` and `TaskbarMode` move to `Tray/Windows/`. `TrayIconService` sets `Icon` and `MacOSProperties.IsTemplateIcon` together. Verify: `TrayIconServiceTests` pass headless with a fake icon set, and the Windows set's tests still pick each status icon for the light and the dark taskbar.
- [x] 1.3 Add `net10.0` with RID `osx-arm64` next to the Windows framework (D2). Include `**/Windows/**` only for the Windows framework and `**/MacOS/**` only for the macOS one. CsWin32, `NativeMethods.txt`, `Avalonia.Win32`, `NAudio.Wasapi`, `TranscribeCppSharp.Native.win-x64`, the manifest, the ICO resources and the WinRT exclusions apply only to Windows. Add `Avalonia.Native` and `TranscribeCppSharp.Native.osx-arm64` (pinned `[0.2.3]` like the Windows one, with its comment) for macOS. Set `EnableWindowsTargeting`, so both frameworks build on both hosts. Add the comment next to the ONNX Runtime pin that its dylib's `minos` sets the minimum macOS (D1). Verify: `dotnet build Pisum.Transcribe.slnx` builds both frameworks with no warnings, on Windows and on the Mac.
- [x] 1.4 Register per platform in `AppHost.Create` (D14): skip `AddRecording`'s capture, `AddVoiceActivity`, `AddTextInsertion` and `AddDictation` on macOS with `#if WINDOWS`, and put the registrations of `Windows/` types in each `Add<Feature>()` under `#if WINDOWS` as well. Verify: on the macOS framework, a test builds the host's service collection and resolves every hosted service, `ITrayIconService`, `INotifier` and `ISettingsStore` without an exception.
- [x] 1.5 Add `InactivePushToTalkHotkey` in `Recording/MacOS/` (D14). It accepts `SetHotkey`, `Suspend` and `Resume`, and raises no events. Register it as `IPushToTalkHotkey` on macOS. Verify: a unit test calls all three methods and sees no event raised.
- [x] 1.6 Make `IStartupRegistration` optional for `SettingsViewModel` and `SettingsWindowService`, and hide **Start with Windows** when it's missing (spec `settings-window`, "Start with Windows"). Register it on Windows only. Verify: a headless `SettingsDialogTests` case without a registration finds the row hidden, and saving works. The existing Windows cases pass unchanged.
- [x] 1.7 Let the test project target the host's framework only (D2): `net10.0-windows10.0.19041.0` on Windows and `net10.0` on macOS. Move test files that call Win32 or test `Windows/` code into `Windows/` folders of the test project and leave those out on macOS (D14): at least `TestWindow`, `RawClipboard`, the Win32 clipboard, text insertion, overlay and recording hardware tests, and `SharpHookKeyboardInputTests`. Add an entry to both `.csproj.DotSettings` files for every new `Windows/` and `MacOS/` folder. Verify: `dotnet test Pisum.Transcribe.slnx` passes on Windows with the same number of tests as before, and passes on the Mac.

## 2. Paths and the single-instance guard

- [x] 2.1 On macOS, `AppPaths.LogsDirectory` is `~/Library/Logs/Pisum Transcribe/`, and `EnsureRootExists` also creates the logs folder when it lies outside the root (D7). The root stays `LocalApplicationData`, which is `~/Library/Application Support` there. Update the class's XML doc. Verify: unit tests on the Mac check both paths and that both folders are created.
- [x] 2.2 Let `SingleInstanceGuard` take `NamedWaitHandleOptions` from the caller (D6). `Program.Main` passes `Local\Pisum.Transcribe.SingleInstance` on Windows, and `Pisum.Transcribe.SingleInstance` with `CurrentUserOnly = true, CurrentSessionOnly = false` on macOS. Verify: on the Mac, an integration test holds the guard and runs a child process that tries it, once in the same session and once through `setsid`, and both are refused. A second test kills the owning child and acquires the abandoned mutex.

## 3. The Swift helper

- [x] 3.1 Add `src/Pisum.Transcribe.MacNative/` with plain `.swift` files (D3) and `pisum_abi_version` and `pisum_free`. Add the MSBuild target that runs `swiftc -emit-library -O -target arm64-apple-macos14 -module-name PisumMac` for the macOS framework on a macOS host only, with `Inputs` and `Outputs`, and copies `libPisumMac.dylib` to the output. Verify: on the Mac, a second `dotnet build` doesn't run `swiftc` again, `otool -l` shows `minos 14.0`, and the Windows build doesn't run the target.
- [x] 3.2 Add the C# wrapper in a `MacOS/` folder, and check `pisum_abi_version` at startup. A mismatch is logged as an error with both versions, and the helper isn't called after that. Verify: a macOS `Integration` test calls the real dylib and gets the expected version. A unit test with a fake version sees the log entry.

## 4. The dev `.app` and signing

- [x] 4.1 Render `AppIcon.icns` from `TrayIcon.svg` in `tools/generate-tray-icon.cs` (D10): the ICNS container written by the tool itself, with PNG entries from 16 to 1024 px. Verify: `dotnet run tools/generate-tray-icon.cs` writes the file, and `sips -g all` on the Mac reads it. The other generated icons don't change.
- [x] 4.2 Add the `Info.plist` template (D9): `CFBundleIdentifier` `io.github.mschnecke.pisum-transcribe`, the name "Pisum Transcribe", `CFBundleExecutable` `Pisum.Transcribe`, both versions from `$(Version)`, `LSMinimumSystemVersion` `14.0`, `LSUIElement` `true` and `CFBundleIconFile` `AppIcon`. Verify: `plutil -lint` passes on the generated plist.
- [x] 4.3 Add the target that assembles `<output>/Pisum Transcribe.app` after the build on a macOS host (D9), and signs it inside-out with `codesign --force --sign $(PisumCodesignIdentity)`, with `-` (ad hoc) as the default and no `--deep`. Verify: `codesign --verify --strict` passes on the bundle, once ad hoc and once with a local self-signed identity, and `codesign -d -r-` shows the certificate leaf in the designated requirement for the latter.
- [x] 4.4 Make `dotnet run --project src/Pisum.Transcribe -f net10.0` open the bundle on macOS (`RunCommand` `open`, `RunArguments` `-W "<app>"`), and keep `UseWin32()` on Windows (D9). With two frameworks, `dotnet run` needs `-f` on both platforms: `-f net10.0-windows10.0.19041.0` on Windows. `CLAUDE.md` and the README say so. On macOS, `Program.Main` uses `UseAvaloniaNative()` and `MacOSPlatformOptions { ShowInDock = false }` (D4). Verify: `dotnet run -f net10.0` on the Mac starts the app, the log in `~/Library/Logs/Pisum Transcribe/` shows the version, and `dotnet run -f net10.0-windows10.0.19041.0` on Windows behaves as before.

## 5. The menu bar and Quit

- [x] 5.1 Add the macOS icon set (D4, D14): the PNGs in `Tray/MacOS/` at `@1x` and `@2x`, templates for ready and unavailable, and the red and amber glyphs for recording and transcribing. Verify: unit tests check each status's image and template flag.
- [x] 5.2 Label the last menu item **Quit Pisum Transcribe** on macOS and **Exit** on Windows, and don't use `Clicked` on macOS (spec `app-shell` "Exit from tray", spec `settings-window` "Opening the settings window"). macOS keeps `NativeMenu.Opening` for the menu's updates (spike M1). Verify: `TrayIconServiceTests` check the label per platform, and on the Mac a click on the icon opens the menu with **Settings…** and **Quit Pisum Transcribe** last.

## 6. The end of the session and `SIGTERM`

- [x] 6.1 Add `pisum_current_quit_sender_pid()` to the helper (D5). It reads `keySenderPIDAttr` from `NSAppleEventManager.currentAppleEvent`, and returns 0 without a current event. The C# wrapper gets the sender's name with libc's `proc_name`. Verify: a macOS `Integration` test outside a quit event gets 0, and a test gets its own process name from `proc_name` with its own pid.
- [x] 6.2 On macOS, the `ShutdownRequested` handler asks for the quit event's sender: `loginwindow` ends with `SessionEnd`, and any other sender with `UserExit` (D5). It keeps waiting through `DispatcherWait.Until` and never cancels. Verify: a unit test with a fake sender source covers both. On the Mac, a quit sent with `osascript` leaves `Shutting down, reason "UserExit"` in the log. The logout is checked in task 10.2.
- [x] 6.3 Add `ShutdownReason.TerminationRequest`, handled like `UserExit` (D5), and on macOS register `PosixSignalRegistration` for `SIGTERM`: cancel the default handling and call `RequestShutdownAsync(ShutdownReason.TerminationRequest)`. Verify: `ShutdownCoordinatorTests` cover the new reason's exit code 0 and the icon removed at once, and a macOS `Integration` test sends `SIGTERM` to the dev bundle's process and sees `Shutting down, reason "TerminationRequest"` in the log and the process gone within 5 s.
- [x] 6.4 On macOS, `ExitProcess` calls libc's `_exit(code)` after `Log.CloseAndFlush()` (D5). Verify: **Quit Pisum Transcribe** ends the dev bundle with exit code 0 within 5 s, and the log ends with the shutdown entries.

## 7. Notifications

- [x] 7.1 Add the helper's notification functions (D8): ask for `alert` and `sound` authorization once, `pisum_notify(title, body)` with a `UNNotificationRequest` without actions, and a delegate that shows banners while the app is active. Without a bundle identifier, each function returns a status instead of calling `UNUserNotificationCenter`. Verify: a macOS `Integration` test run from the test host, which has no bundle, gets the "no bundle" status and no crash.
- [x] 7.2 Add `MacNotifier : INotifier` in `Notifications/MacOS/` and register it on macOS (D8). It moves to the UI thread through `IUiDispatcher`, asks for authorization at the first start, logs the notification when there's no bundle, and logs at Debug when the user refused. Verify: unit tests with a fake helper cover the three outcomes, and on the Mac the dev bundle shows the permission prompt once and a notification from "Pisum Transcribe" with its icon.

## 8. Continuous integration

- [x] 8.1 Add a `macos-latest` job to `ci.yml` (D13): checkout, `setup-dotnet` from `global.json`, restore, `dotnet build Pisum.Transcribe.slnx --no-restore` and `dotnet test Pisum.Transcribe.slnx`, with no artifact. The Windows job stays as it is. Verify: both jobs pass on the pull request, and the macOS job's log shows `swiftc` and the ad-hoc signing of the bundle.

## 9. Documentation

- [x] 9.1 Update `CLAUDE.md` (proposal, Impact): the layout (`MacNative/`, the `MacOS/` folders, `Info.plist`), the two frameworks and which one the test project builds, the macOS commands, the Command Line Tools as the only requirement next to the SDK, and how to create a local self-signed signing identity and set `PisumCodesignIdentity`. Verify: a developer on a fresh Mac with the SDK and the Command Line Tools can follow it to a running dev bundle with a stable identity.
- [x] 9.2 Update `README.md` (macOS is coming, no install instructions yet) and mark `add-macos-shell` done in `docs/roadmap.md`. Verify: both files mention macOS as described, and no install steps for macOS.

## 10. Regression pass

- [ ] 10.1 Run `dotnet build Pisum.Transcribe.slnx` and `dotnet test Pisum.Transcribe.slnx` on Windows and on the Mac, the Windows `Hardware` tests, and `openspec validate add-macos-shell --strict`. Verify: no warnings, all tests pass, and validation reports no issues.
- [ ] 10.2 Check by hand on the Mac with the dev bundle, signed with a local identity (Migration Plan step 3). Verify each:
  - the menu bar icon in light and dark mode, no Dock icon, and no entry in the app switcher
  - **Quit Pisum Transcribe** ends the app within 5 s
  - a real logout while the app runs: no delay, no "interrupted" message, `SessionEnd` in the log despite the `SIGTERM` that follows, and the shutdown's entries complete up to the last one before `_exit`
  - a restart while the app runs, with the same outcome
  - a second launch from Finder, from the terminal and through `open -n` leaves one icon
  - after `kill -9`, the app starts again at once
  - the notification permission prompt at the first start, and a notification afterwards
  - ten launches through `open` in a row, with no startup abort (the spike's unexplained crash, Risks)
- [ ] 10.3 Check on Windows 11 that nothing changed: dictation end to end, **Exit**, the settings window with **Start with Windows**, and a notification. Verify each by hand with a build from the branch.
