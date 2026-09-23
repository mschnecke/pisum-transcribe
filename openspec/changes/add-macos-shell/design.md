## Context

See proposal.md, Why. This change starts after `move-windows-shell-to-avalonia` and the four Windows changes that prepare it. It assumes:
- **`extract-ui-seams`:** `IUiDispatcher`, `INotifier`, `TrayStatus`, and Windows-only code in `Windows/` subfolders
- **`add-monochrome-tray-icons`:** the shared glyph set, including the macOS PNGs
- **`show-windows-notifications`:** the notifications behind `INotifier`
- **`move-windows-shell-to-avalonia`:** the Avalonia 12.1 shell with `TrayIcon`, and `DispatcherWait` over Avalonia's `DispatcherFrame`

It also depends on the spike of `move-windows-shell-to-avalonia` (D3 there): M1 (agent app, template tray icon, menu updates) and M4 (**Quit** and logout through `ShutdownRequested`).

This design also records the decisions that hold for every macOS change, decided in explore mode on 2026-09-22. The section **Decided for later macOS changes** lists the ones that belong to later steps.

**Facts checked on the development Mac (Apple silicon, macOS 27, .NET SDK 10.0.401, Command Line Tools with Swift 6.4, `codesign` and `notarytool`, no Xcode):**
- **Native packages.** Every native dependency ships for `osx-arm64`:
  - TranscribeCppSharp.Native 0.2.3, with `libggml-metal.dylib`, whose `contract.json` lists the backends `metal` and `cpu`
  - ONNX Runtime 1.30.0
  - SharpHook 8.0.0 (libuiohook)
  - Avalonia.Native, SkiaSharp and HarfBuzzSharp
- **Minimum macOS versions** (`LC_BUILD_VERSION minos`): `libonnxruntime.dylib` needs **14.0**. `libtranscribe.dylib`, `libAvaloniaNative.dylib`, `libuiohook.dylib`, `libSkiaSharp.dylib` and `libHarfBuzzSharp.dylib` need 11.0.
- **Data folder:** `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` returns `~/Library/Application Support`, so `AppPaths.Root` is already right on macOS.
- **The single-instance mutex.** A named `Mutex` with a `Local\` name is scoped to the **Unix session** on macOS. A process started in a new session (`setsid`), as Finder and launchd start apps, acquired `Local\Pisum.Transcribe.SpikeTest` while a terminal-started process held it.
  - With `new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = false }`, a second process was refused in the same session and in a new one.
  - The options' defaults are `CurrentUserOnly = true` and `CurrentSessionOnly = true`.
- **SharpHook** has `KeyCode.VcRightMeta` (right Command) and `KeyCode.VcFunction` (fn/Globe, "Available on: macOS").
- **The spike of `move-windows-shell-to-avalonia`** (2026-09-22, branch `spike/avalonia-shell`; M1–M4 are in that design's Context). For this change and the later ones:
  - **Quit reason.** Inside `ShutdownRequested`, `NSAppleEventManager.currentAppleEvent` is the quit event itself: `'aevt'/'quit'`.
    - A quit with the logout reason carried `'why?'` = `'rlgo'`, and a plain quit carried none.
    - Avalonia's `IsOSShutdown` is `internal`, and it was `false` for both.
  - **`SIGTERM`**, handled through `PosixSignalRegistration`, ended the spike in 0.11 s. `lifetime.Shutdown()` doesn't raise `ShutdownRequested`.
  - **The Accessibility grant needs a restart.** SharpHook's libuiohook checks `CGPreflightPostEventAccess()`, which doesn't see a grant made while the process runs: two restarts of the hook failed, and a relaunch worked. `AXIsProcessTrusted()` returns true at once, so it can't serve as the signal.
  - **M5, pasteboard privacy:**
    - Without the developer preview, macOS 27 reports `accessBehavior` = 2 (always allow) and never alerts.
    - With the preview on, the first read of another app's content alerted and **blocked the reading thread** until the alert was answered (4.9 s, then 7.2 s). The state went from 0 (default) to 1 (ask) and stayed there, so every later read alerted again.
    - Reading the app's own content didn't alert. Writing and `changeCount` never alerted.
  - **M6, a stable self-signed certificate:**
    - `codesign` accepted an untrusted self-signed identity from a separate keychain, with no trust setting and no prompt. The keychain's key partition list is set for `codesign`.
    - The designated requirement was `identifier "…" and certificate leaf = H"…"`, and the Accessibility grant survived four rebuilds with different CDHashes.
    - OpenSSL 3's default `.p12` fails `security import` with "MAC verification failed". It has to be exported with `-certpbe PBE-SHA1-3DES -keypbe PBE-SHA1-3DES -macalg sha1`.
  - **One unexplained crash:** a startup abort from an unhandled managed exception, once in eight launches, not reproducible.
- **Intel Macs:** macOS 26 was the last release for them, so current macOS runs only on Apple silicon.
- **AppKit's `NSPasteboard.h`** in the Command Line Tools' SDK:
  - It has `NSPasteboardContentsCurrentHostOnly` for `prepareForNewContentsWithOptions:` ("should not be available to other devices"), which keeps an entry off Universal Clipboard.
  - It documents `accessBehavior` (macOS 15.4+): the general pasteboard reports `.default` until the app's first pasteboard access alert, then `.ask`. From then on the app is listed in System Settings, where the user can choose *ask*, *always allow* or *always deny*.

**pisum-whisper as the precedent** (`mschnecke/pisum-whisper`, the sister project, which already ships on macOS):
- **Avalonia as a menu bar app:** it runs with both `LSUIElement` in its Info.plist and `MacOSPlatformOptions { ShowInDock = false }` in `BuildAvaloniaApp`. The plist key covers a bundled launch, and the Avalonia option covers `dotnet run` without a bundle.
- **Packaging** (its `add-packaging-ci` D5 and D6):
  - `build-app.sh` assembles the `.app` from a self-contained ReadyToRun publish, and signs it ad hoc with the real identifier.
  - `build-pkg.sh` runs `pkgbuild` and `productbuild` without `--sign`, installing into `/Applications`.
  - A `postinstall` run as root removes the quarantine from the app, so Gatekeeper never assesses it.
  - Its design records the cost of ad hoc signing as its largest user-facing one: the Accessibility grant is lost on every update.
- **Platform code:**
  - NSPasteboard through hand-written `objc_msgSend`, with autorelease pools.
  - `AXIsProcessTrusted` through P/Invoke.
  - Start at login through a LaunchAgent plist in `~/Library/LaunchAgents`.
  - Its `MacOsClipboard` states that nothing public keeps an entry off Universal Clipboard. The SDK header above shows otherwise.
- **Distribution:** `release.yml` fans out through `repository_dispatch` to the Homebrew tap `mschnecke/homebrew-pisum-whisper`, whose cask installs the `.pkg`.
- **Development runs:** its bootstrap found that the permission grant of a `dotnet run` build belongs to Rider, not to the app.

## Goals / Non-Goals

**Goals:**
- The Mac build starts, sits in the menu bar, and ends as the `app-shell` spec requires, with the same 5 s budget and the same `ShutdownCoordinator`.
- The Windows build and its behavior don't change. The Windows job in CI stays green, and it also compiles the macOS target.
- Building on a Mac needs only the .NET SDK and the Command Line Tools.
- The decisions every later macOS change relies on are made once, here.

**Non-Goals:**
- The release `.pkg` and its signing, in `add-macos-packaging`.
- Developer ID signing and notarization, in any change. There is no Apple Developer Program membership (user decision).
- Any permission prompt other than the one for notifications (D8).
- A universal (arm64 + x86_64) build.

## Decisions

### D1: Apple silicon, macOS 14 or later

- **Architecture:** the Mac build is `osx-arm64` only. Current macOS no longer runs on Intel.
- **Minimum version:** macOS 14 Sonoma, set by ONNX Runtime 1.30.0, which Silero VAD needs. The other native libraries allow 11.0.
  - `LSMinimumSystemVersion` is `14.0`, and the Swift helper targets `arm64-apple-macos14`.
  - Everything the macOS changes need is available from 14 on: `UNUserNotificationCenter`, `SMAppService` (13+) and `AVAudioApplication` (14+).
  - A comment next to the ONNX Runtime pin in `Directory.Packages.props` says to re-read the dylib's `minos` when the pin changes.

*Rejected:*
- **Also x86_64:** there's no current macOS to test it on, and it would double the native payload.
- **A lower floor:** it would need an older ONNX Runtime, and the pin exists for a reason.

### D2: Two target frameworks, and what each host builds

- **Frameworks:** the app project targets `net10.0-windows10.0.19041.0` (RID `win-x64`) and `net10.0` (RID `osx-arm64`).
- **Folders:**
  - `**/Windows/**` compiles only for the Windows framework, and `**/MacOS/**` only for the macOS one.
  - Files in both keep their feature's namespace (`extract-ui-seams` D4).
  - Registration in each `Add<Feature>()` picks the implementation with `#if` on the target framework.
- **Packages per framework:**
  - Windows: `Avalonia.Win32`, `NAudio.Wasapi`, `TranscribeCppSharp.Native.win-x64`, and CsWin32 with `NativeMethods.txt`.
  - macOS: `Avalonia.Native` and `TranscribeCppSharp.Native.osx-arm64`.
  - ONNX Runtime and SharpHook carry both runtimes in one package.
- **What each host builds:**
  - The app project builds **both frameworks on every host**, with `EnableWindowsTargeting` on macOS. An edit on the Mac then can't break the Windows compile unnoticed, and the Windows CI job compiles the macOS code too.
  - The Swift helper (D3) and the dev `.app` (D9) are built only on a macOS host.
  - The **test project targets only the host's framework**, so `dotnet test Pisum.Transcribe.slnx` stays one command on each machine. CI runs both.
- **`CLAUDE.md`** gets the macOS commands next to the Windows ones.

*Rejected:*
- **One `net10.0` framework for both, with runtime OS checks:** Windows needs the WinRT projections for toasts.
- **`net10.0-macos`:** it needs Xcode and the `macos` workload (explore-mode decision, Swift helper instead).
- **Building only the host's framework in the app project too:** faster, but Mac-only development could then break Windows without anyone noticing until CI.

### D3: The Swift helper `libPisumMac.dylib`

- **The split:**
  - Apple's C APIs are called from C# through P/Invoke: AudioToolbox, the Accessibility C API, CoreGraphics events, `IsSecureEventInputEnabled`, CFNotificationCenter and libc.
  - The Objective-C APIs go through the helper: `UNUserNotificationCenter` here, and later `NSPasteboard`, `SMAppService`, `AVCaptureDevice` and perhaps `NSPanel`.
- **Sources** are in `src/Pisum.Transcribe.MacNative/`: plain `.swift` files, one per area, with no Xcode project and no Swift package.
- **The build:**
  - An MSBuild target in `Pisum.Transcribe.csproj` runs, for the macOS framework on a macOS host only: `swiftc -emit-library -O -target arm64-apple-macos14 -module-name PisumMac -o <obj>/libPisumMac.dylib <sources>`.
  - It has `Inputs` and `Outputs` for incremental builds, and it copies the dylib to the output as a native library.
- **ABI rules:**
  - Only `@_cdecl` functions with C types cross the boundary: UTF-8 `const char*` in, `int32` status codes out.
  - Memory Swift allocates for the caller is freed with `pisum_free`.
  - Callbacks are `@convention(c)` function pointers with a `void*` context. No Objective-C or Swift object crosses the boundary.
  - Functions that touch AppKit or `UNUserNotificationCenter`'s delegate are called on the main thread. On macOS that's Avalonia's UI thread, reached through `IUiDispatcher`.
  - `pisum_abi_version()` returns a constant that C# checks at startup. A stale dylib then fails with a clear log entry instead of undefined behavior.
- **The Swift runtime** comes from the OS (ABI-stable since macOS 10.14.4), so nothing is bundled.
- **In this change** the helper has `pisum_abi_version`, `pisum_free`, and the notification functions (D8).
- **Tests:** macOS `Integration` tests call it through the C# wrappers and skip on Windows.

*Rejected (explore mode):*
- **Hand-written `objc_msgSend` bindings in C#:** blocks and delegates by hand, and a wrong signature crashes at runtime instead of failing to compile.
- **`net10.0-macos` with Microsoft's bindings:** it needs Xcode and the workload, and Avalonia.Native and the macOS runtime would both drive `NSApplication` in one process, which is unknown territory.

### D4: An agent app with a template icon in the menu bar

- **Info.plist:** `LSUIElement` = `true`, so there is no Dock icon, no app switcher entry and no app menu.
- **`MacOSPlatformOptions { ShowInDock = false }`** in `BuildAvaloniaApp` does the same for a `dotnet run` without a bundle, as in pisum-whisper. Both stay, because each covers one of the two ways the app is launched.
- **The icon:**
  - It is the glyph set shared with Windows (`add-monochrome-tray-icons` D1), rendered for macOS at 18 pt (`@1x` and `@2x`).
  - Ready and unavailable are template images (`MacOSProperties.IsTemplateIcon` = `true`), so macOS draws them in the menu bar's color. Unavailable is at 50 % alpha.
  - Recording and transcribing are the red and amber glyphs, not templates.
  - `TrayIconService` switches `IsTemplateIcon` with each status, as the spike's M1 checks.
  - The tooltip carries the status, as on Windows.
- **The menu:**
  - The same `NativeMenu` as on Windows.
  - The last item's label depends on the platform: **Exit** on Windows, **Quit Pisum Transcribe** on macOS, as Apple's guidelines name it.
  - A click on the icon opens the menu, which is the macOS convention. `Clicked` isn't used on macOS.

### D5: Quit and the end of the session

- **Quit:** **Quit Pisum Transcribe** calls `ShutdownCoordinator.RequestShutdownAsync(ShutdownReason.UserExit)`, as **Exit** does.
- **The end of the session** (log out, shut down, restart):
  - macOS sends the app a quit event, and Avalonia turns it into `ShutdownRequested`.
  - The same handler as on Windows waits with `DispatcherWait.Until(RequestShutdownAsync(reason))` and never cancels, so the logout isn't blocked.
  - **The reason comes from the quit Apple event**, because Avalonia's `IsOSShutdown` is `internal` (spike, Context):
    - The handler asks the helper for `pisum_current_quit_reason()`, which reads `'why?'` from `NSAppleEventManager.currentAppleEvent`.
    - `'rlgo'` or `'logo'` (log out), `'rest'` (restart) and `'shut'` (shut down) mean `SessionEnd`. No reason means `UserExit`, as when a user quits the app in Activity Monitor.
  - The log says the app ended because the macOS session ended.
- **Ending the process:** on macOS, `ExitProcess` calls libc's `_exit(code)` after `Log.CloseAndFlush()`. It's the counterpart of `TerminateProcess`: no process-exit handlers, so the 5 s budget holds.
- **A real logout** is still to be checked by hand, because the spike sent the logout-reason event itself. If a real logout carries no reason, the fallback is `NSWorkspace.willPowerOffNotification` through the helper, which marks the next `ShutdownRequested` as `SessionEnd`. The spec requires only the outcome.
- **`SIGTERM`:**
  - On macOS, the app registers `PosixSignalRegistration` for `SIGTERM`. The handler cancels the default handling and calls `ShutdownCoordinator.RequestShutdownAsync(ShutdownReason.TerminationRequest)`, the same path as **Quit**, within 5 s.
  - `TerminationRequest` is a new `ShutdownReason`, handled like `UserExit` (exit code 0, the icon removed at once). It exists so that the log's `Shutting down, reason "TerminationRequest"` names the termination request, as the `app-shell` spec's "Termination request on macOS" requires.
  - This serves the `.pkg`'s `preinstall`, which ends a running app before its files are replaced (see `add-macos-packaging` below). It also backs up the end of the session, because launchd sends `SIGTERM` to processes that are still running.
  - It calls `ShutdownCoordinator` directly, not `lifetime.Shutdown()`, which skips `ShutdownRequested` and with it the wait for background work.
  - A test sends `SIGTERM` to the dev bundle and checks the log entry and the time to exit. The spike's handler ended the process in 0.11 s.

### D6: One instance per user

`SingleInstanceGuard` takes the scope from the caller.

| Platform | Mutex | Why |
|---|---|---|
| Windows | `Local\Pisum.Transcribe.SingleInstance`, as today | One instance per Windows session, as the spec says |
| macOS | `Pisum.Transcribe.SingleInstance` with `CurrentUserOnly = true, CurrentSessionOnly = false` | `Local\` is scoped to the Unix session, so launches from Finder, from login and from a terminal would each get their own instance (Context) |

- **Unchanged:** the 6 s wait and the handling of an abandoned mutex after a crash. A test checks that the mutex is abandoned when its owner is killed.
- **LaunchServices** already activates a running agent app instead of starting a second one when it's opened from Finder. The mutex covers `open -n`, a start of the binary, and `dotnet run`.

*Rejected:*
- **A lock file with `FileShare.None` in the data folder:** it works too, but the mutex with options keeps one implementation and its tests.

### D7: Where data and logs live on macOS

| | Windows | macOS |
|---|---|---|
| Settings, models (`AppPaths.Root`) | `%LOCALAPPDATA%\Pisum Transcribe\` | `~/Library/Application Support/Pisum Transcribe/` |
| Logs (`AppPaths.LogsDirectory`) | `<root>\logs\` | `~/Library/Logs/Pisum Transcribe/` |

- **Models** stay in Application Support, not in `~/Library/Caches`, which macOS may purge.
- **`EnsureRootExists`** also creates the logs folder when it lies outside the root.
- **Nothing is synced:** Application Support isn't synced by iCloud unless the user moves their Library, and the app never writes to iCloud Drive.

### D8: Notifications on macOS

- **`MacNotifier : INotifier`** calls `pisum_notify(title, body)` in the helper, which adds a `UNNotificationRequest` with no actions.
- **Permission:**
  - The helper asks for `alert` and `sound` authorization once, at the first start.
  - `add-macos-setup` moves that into the setup window.
  - If the user refuses, notifications are logged at Debug and dropped, as on Windows when notifications are off.
- **A delegate** set by the helper returns `banner` and `list` from `willPresent`, so notifications also show while the app is active, for example with the settings window in front.
- **Outside a bundle:** `UNUserNotificationCenter` needs a bundled app with a bundle identifier. The helper checks for one and returns a status instead of calling it. Outside the `.app`, notifications are logged only (D9).
- **Identity:** they show the bundle's name and `AppIcon.icns`.

### D9: The dev `.app` and a stable signing identity

- **The bundle:** on a macOS host, a target after the build assembles `<output>/Pisum Transcribe.app`:
  - `Contents/MacOS/`, with the build output and the apphost `Pisum.Transcribe` as `CFBundleExecutable`
  - `Contents/Resources/AppIcon.icns`
  - `Contents/Info.plist`, from a template in the repository: `CFBundleIdentifier` `io.github.mschnecke.pisum-transcribe`, the name "Pisum Transcribe", `CFBundleShortVersionString` and `CFBundleVersion` from `$(Version)`, `LSMinimumSystemVersion` `14.0` and `LSUIElement` `true`
  - Later changes add their keys: `NSMicrophoneUsageDescription` comes with `add-macos-recording`.
- **Signing:** one mechanism for development and release builds.
  - The bundle is signed inside-out with `codesign --force --sign <identity>`, with no `--deep`.
  - The identity is the property `PisumCodesignIdentity`, which a developer sets through the environment variable of the same name. The default is `-`, meaning ad hoc.
  - macOS remembers permission grants against the app's designated requirement:
    - For an ad hoc signature, that's the build's hash, so every build is a new app to macOS, and Accessibility and Microphone have to be granted again.
    - For a certificate, it's the bundle identifier and the certificate, which stay the same across builds.
  - **Developers** use any local self-signed code-signing certificate in the login keychain, or ad hoc. `CLAUDE.md` says how to create one.
  - **Releases** are signed with **the project's own self-signed code-signing certificate** (user decision), so users' grants survive updates:
    - It is created once, with a long validity.
    - It is kept as a CI secret (`.p12` and password), with a backup outside GitHub.
    - It is as permanent as the bundle identifier.
    - `add-macos-packaging` imports it into a temporary keychain on the runner and sets the key's partition list for `codesign`. No trust setting is needed (spike M6, Context).
- **`dotnet run`** on macOS opens the bundle through LaunchServices (`RunCommand` `open`, `RunArguments` `-W "<app>"`). The app, not the terminal, is then the process that permissions and notifications belong to, and the permission flow later changes add can be tested for real. The log is in `~/Library/Logs/Pisum Transcribe/`.
- **The release bundle** is a different one: `add-macos-packaging` builds it from the publish output, signs it with the project's certificate, and wraps it in the unsigned `.pkg`. There is no Developer ID and no notarization.

### D10: Icons for macOS

- **The menu bar glyphs** come from the shared set (`add-monochrome-tray-icons` D1 and D3), each at 18 × 18 and 36 × 36:
  - template PNGs (black with alpha) for ready and unavailable
  - red and amber PNGs for recording and transcribing
- **`AppIcon.icns`** is the app icon for Finder, notifications and the bundle. The same tool renders it from `TrayIcon.svg`, and it runs on SkiaSharp, so it works on both platforms.
- **The tool writes the ICNS container itself.** It's a list of PNG entries from 16 to 1024 px, so it doesn't need `iconutil`.

### D11: How the specs cover two platforms

This rule holds for every macOS change:
- **Same behavior with a different platform detail:** one requirement, with the detail named for each platform. The requirement is MODIFIED and keeps its name. Example: "Local data folder" names both folders.
- **Behavior that exists on one platform only:** a requirement of its own, named for the platform. It is ADDED, and the other platform's requirement stays untouched. Examples: "End with the macOS session" next to "End with the Windows session", and later "Secure input on macOS" next to "Elevated target windows".
- **Something only one platform has, that fits no capability:** a new capability, such as the macOS permissions in `add-macos-setup`.
- **Scenarios** name the platform in their WHEN when they apply to one platform only.

### D12: Lockstep releases

This constrains `add-macos-packaging`, and it is recorded here because CI starts building macOS now:
- One version tag gives one GitHub release carrying the MSI and the `.pkg`, and it is published only when both builds and their tests pass.
- `UpdateCheckService` stays unchanged. It compares the latest release's version and opens its page, and that page carries both installers.
- The cost: a failing macOS build also holds back a Windows fix, and the other way round.

### D13: CI

- **`ci.yml`** gets a job on `macos-latest` (Apple silicon, with Xcode, so `swiftc` is there): checkout, `setup-dotnet` from `global.json`, `dotnet build Pisum.Transcribe.slnx`, `dotnet test Pisum.Transcribe.slnx`.
  - The build includes the Swift helper and the ad-hoc-signed dev `.app`, so both targets are checked on every change.
  - It uploads no artifact.
- **The Windows job** stays as it is, and now also compiles the macOS framework (D2).
- **Hardware tests** run in neither job, as the `packaging` spec requires.

### D14: The Mac build runs only the shell

Until the later macOS changes add their features, the Mac build registers only the shell. Every feature folder still compiles for both frameworks, because `Settings` and `SettingsWindow` use the types of the skipped features (`Hotkey`, `InsertionMethod`, `IPushToTalkHotkey`). Only the `Windows/` and `MacOS/` subfolders are left out (D2).

**Registration in `AppHost.Create`:**

| Feature | macOS in this change | Added on macOS by |
|---|---|---|
| Tray | yes, the menu bar icon (D4) | – |
| Notifications | `MacNotifier` (D8) | – |
| Settings, Updates | yes | – |
| SpeechModels | yes: the setup window and **Download model…** | – |
| Transcription | yes, CPU only: it loads and warms up the model, which checks the `osx-arm64` native package early | `add-metal-backend` |
| SettingsWindow | yes, with the gaps below | – |
| Recording | only the inactive hotkey (below) | `add-macos-recording` |
| VoiceActivity, Dictation | no | `add-macos-dictation` |
| TextInsertion | no | `add-macos-text-insertion` |

- `AppHost.Create` skips the four features with `#if WINDOWS`, which the Windows framework defines and `net10.0` doesn't.
- Inside each `Add<Feature>()`, the registrations of `Windows/` types sit under `#if WINDOWS` as well (D2), so the methods compile on macOS.
- The menu on macOS therefore has no **Cancel transcription** yet. That item belongs to Dictation.

**The settings window on macOS in this change:**
- **Hotkey:**
  - `SettingsApplier` and the hotkey editor need `IPushToTalkHotkey`. SharpHook isn't started on macOS yet, because its hook needs the Accessibility grant (spike M3), which `add-macos-setup` asks for (Non-Goals).
  - macOS registers `InactivePushToTalkHotkey` in `Recording/MacOS/`. It accepts `SetHotkey`, `Suspend` and `Resume`, and raises no events, so the hotkey editor records nothing.
  - `add-macos-recording` replaces it with SharpHook and deletes it.
- **Start at login:** `IStartupRegistration` has no macOS implementation until `add-macos-login-item`. `SettingsViewModel` takes it as optional, and hides the **Start with Windows** row when it's missing.
- **Dictation and Text insertion sections:** they stay, and their settings are saved, but nothing reads them on macOS yet.
- **Backend:** the default setting checks whether Vulkan is available and falls back to the CPU, so it works on macOS. The **Vulkan** choice stays in the list, and choosing it makes the model load fail with the "transcription failed" notification, because the macOS native package has no Vulkan backend. This is accepted until `add-metal-backend` replaces the choice with a GPU backend.

**Win32 calls in shared files** move into `Windows/` subfolders, so the macOS compile never sees them:
- `App.ExitProcess`: `TerminateProcess` on Windows and `_exit` on macOS (D5), each in its platform folder.
- `TextInsertion/SharpHookKeyboardInput` (`GetAsyncKeyState`) moves to `TextInsertion/Windows/`. `add-macos-text-insertion` adds the macOS counterpart.
- `TextInserter`'s default for `isSelfElevated`, which calls `ProcessElevation`, moves into the Windows registration.
- `RecordingOverlayWindow`: the placement on the target window's monitor and the extended styles move into `Dictation/Windows/`, behind a seam that the window calls. `add-macos-dictation` adds the macOS side (`pisum_overlay_configure`).
- `TrayIconService`: the icon choice moves behind a per-platform icon set. Each status maps to a `WindowIcon` and a template flag, and the set raises `Changed` when the icon has to be reloaded.
  - Windows: `TrayIcons` and `TaskbarModeWatcher`, as today. `ITaskbarModeWatcher` and `TaskbarMode` move to `Tray/Windows/`.
  - macOS: the PNGs in `Tray/MacOS/`, with the template flag for ready and unavailable (D4). They never change, because macOS tints template images itself.
  - `TrayIconService` sets `Icon` and `MacOSProperties.IsTemplateIcon` together.

**Tests** follow the same folder rule. A test file that calls Win32 or tests `Windows/` code moves into a `Windows/` folder of the test project, and the macOS build leaves it out. `SharpHookKeyboardInputTests` is one of them. Tests of platform-neutral code, such as `DictationController` or `TextInserter` with fakes, keep running on both platforms.

*Rejected:*
- **An "unavailable" stub for every interface of the skipped features:** each stub would be written only to be deleted by a later change, and the tray would claim a dictation feature that doesn't exist yet.
- **CsWin32 on both frameworks, so that the shared files compile unchanged:** a Win32 call in shared code would then fail at runtime on a Mac instead of at build time. That's the check D2 exists for.
- **No settings window on macOS in this change:** choosing the model or the language would need edits to `settings.json`. The two gaps above are smaller.

## Decided for later macOS changes

These were decided in explore mode on 2026-09-22, and each change's own design picks them up:

- **`add-macos-setup`:**
  - One setup window for the speech model and the permissions: Accessibility and Microphone (required), Notifications (optional). The model download runs while the user grants permissions.
  - The permissions are asked up front, never at the first hotkey press. A microphone prompt then would take focus from the target app mid-dictation.
  - The rows update on their own by polling the grant status.
  - The window closes once the model is installed and both required grants are in place. That changes `model-management`'s "First-run setup" for macOS.
  - A tray item appears for a missing or revoked grant, next to **Download model…**.
  - A denied microphone delivers silence instead of an error, so its state is read from `AVCaptureDevice`.
  - **Proposed:** the `models` folder is excluded from Time Machine when it is created, through `CFURLSetResourcePropertyForKey(kCFURLIsExcludedFromBackupKey)`.
    - The exclusion sticks to the folder, so models downloaded later are excluded too. Settings and logs stay in backups.
    - A model is 1–2 GB, can always be downloaded again, and is checked by SHA-256.
    - On Windows, File History doesn't back up `%LOCALAPPDATA%` either.
  - **Free disk space on macOS.** The disk space check uses the *important usage* capacity, which includes purgeable space and matches what Finder shows. `ModelStore` gets it through its existing `getAvailableFreeSpace` delegate, from `CFURLCopyResourcePropertyForKey` with `kCFURLVolumeAvailableCapacityForImportantUsageKey`, a CoreFoundation C API.
    - Measured on the development Mac on 2026-09-22: `DriveInfo.AvailableFreeSpace` (`statfs`, as `ModelStore` uses today) reported 133.00 GB, and the important usage capacity 147.03 GB.
    - On a nearly full Mac with a lot of purgeable space (iCloud Drive's optimized storage, local snapshots, caches), `DriveInfo` would refuse a download that macOS would make room for.
    - Apple's guidance is the important usage capacity for work the user starts. A model download is exactly that.
    - `model-management`'s "Disk space check" goes on this change's checklist: on macOS, "free" means the space available for a download the user starts (D11).
  - **After the Accessibility grant, the app relaunches itself.** The hotkey's permission check only sees the grant in a new process (spike M3, Context). The setup window says so before it relaunches, and it opens again after the relaunch if the model download or another grant is still missing.
  - **A fifth row, Paste from other apps.**
    - It reads the pasteboard once while the user is looking, so the first alert appears there and not in the middle of an insertion, and the app gets listed in System Settings.
    - The alert's **Allow** counts for one read only (spike M5), so the row then leads the user to System Settings to choose **always allow**. It shows as done only in that state.
    - Where the privacy isn't enforced, the row is done at once, as on macOS 27 today, which reports "always allow".
- **`add-macos-text-insertion`:**
  - Paste with restore stays the method on macOS, as on Windows.
  - Before each snapshot, the app checks `NSPasteboard.accessBehavior` (macOS 15.4+). If reading isn't allowed, that dictation is typed instead, so the user's clipboard stays untouched.
    - Pasteboard privacy: macOS alerts when an app reads the pasteboard without direct user interaction. Writing, `changeCount` and the `detect…` methods don't alert.
    - Reports said it was still off by default in macOS 26, and its state in macOS 27 is unknown.
    - There is no API to ask for access ahead of time.
  - Secure Event Input (`IsSecureEventInputEnabled`, for password fields and Terminal's Secure Keyboard Entry) gets its own requirement next to "Elevated target windows" (D11). While it is on, the event tap sees no keys, so the hotkey would silently do nothing, for example in Terminal with Secure Keyboard Entry on. The tray therefore says so (user decision):
    - while no dictation runs, the app calls `IsSecureEventInputEnabled()` every 2 s, a cheap C call
    - while it returns true, the tray shows the dimmed *unavailable* glyph with the tooltip "Pisum Transcribe: paused while secure input is on"
    - there is no notification, because every password field would trigger one
    - the `dictation` spec's "Tray icon states" gets a macOS reason for *unavailable*
  - Clipboard history exclusion:
    - The nspasteboard.org markers (`org.nspasteboard.TransientType`, `ConcealedType`, `AutoGeneratedType`) keep the text out of clipboard managers.
    - `prepareForNewContents(with: .currentHostOnly)` keeps it off Universal Clipboard, the counterpart of the cloud clipboard.
    - Whether Spotlight's clipboard history honors the markers is unknown.
  - "Busy clipboard fallback" becomes Windows-only, because NSPasteboard has no lock.
  - `InsertionTarget` gets an opaque window token that each platform owns, instead of an HWND. On macOS it is the focused application's pid and window element, through the Accessibility C API.
  - Typed text is sent in chunks that fit `CGEventKeyboardSetUnicodeString`.
  - **Requirements to cover in its spec deltas** (D11):
    - `text-insertion`: Elevated target windows (with Secure input on macOS added next to it), Modifier keys released before input, Clipboard paste method, Clipboard restore, Clipboard history exclusion, Busy clipboard fallback (Windows-only), Insertion outcome
    - `dictation`: Insertion fallback notification (the secure-input reason), Tray icon states (the secure-input reason for *unavailable*)
  - **M5 is done** (Context). It sharpens the rule above:
    - A snapshot is taken only when `accessBehavior` is 2 (always allow).
    - In the "ask" state every read alerts and blocks, so that dictation is typed.
    - The pasteboard is never read on the UI thread, because an alert blocks the thread that reads. `changeCount` and writing are safe anywhere.
- **`add-macos-recording`:**
  - The default hotkey on macOS is **right Command** (`VcRightMeta`), and right Ctrl stays the default on Windows.
  - Right Option was rejected: on the German layout and many others, Option types characters such as `@`, `€` and brackets, so every one of them would briefly open the microphone and flash the menu bar's microphone indicator.
  - fn/Globe can be chosen in the hotkey editor, with a hint to set macOS's "Press 🌐 key to" to "Do Nothing".
  - The editor's modifier rule and its labels use Mac names.
  - The hook needs a process that started after the Accessibility grant (spike M3). A hook that fails with `ErrorAxApiDisabled` makes the tray show *unavailable* with the reason, and the setup window's relaunch (`add-macos-setup`) brings it up.
  - **Microphone checks before capture.** A denied microphone, and a muted input device, deliver digital zeros without an error. Under "Start completes when audio flows", the start would then wait the full 3 s and fail as "microphone not responding", the wrong message, 3 s late. So before opening the microphone:
    - the app reads `AVCaptureDevice`'s authorization and fails at once with "Microphone access blocked"
    - it reads `kAudioDevicePropertyMute` (input scope), where the device has it, and reports "Microphone muted"
  - CoreAudio has no "silent packet" flag like WASAPI's, so only digital zeros count as silence. That requirement's Windows wording gets a macOS counterpart (D11).
  - Bluetooth microphones such as AirPods switch the headset to its hands-free profile when capture starts. The 3 s budget and the overlay's starting look cover the delay. A hardware test with AirPods confirms it.
  - The hook's callback stays fast, because events are queued into the controller's channel as on Windows, since macOS disables an event tap whose callback is slow. The spike didn't cover it, so this change checks, in libuiohook's source and with a deliberately slow handler, that the tap is enabled again after `kCGEventTapDisabledByTimeout`.
  - "Missed release recovery" polls `CGEventSourceKeyState` on the HID system state. It still reports the physical key while secure input hides key events from the tap, for example when a password field takes focus during a hold.
  - "Reset on session switch":
    - the screen lock through the distributed notifications `com.apple.screenIsLocked` and `com.apple.screenIsUnlocked` (CFNotificationCenter, a C API)
    - fast user switching through `NSWorkspace`'s session notifications (the helper)
  - **"Simulated key events ignored" on macOS:**
    - Events that software posts with `CGEventPost` carry the poster's pid, and the hook ignores them, as on Windows. That covers the app's own Cmd+V and tools such as Keyboard Maestro, BetterTouchTool and Hammerspoon.
    - A virtual keyboard device, such as Karabiner-Elements', can't be told apart from hardware, so it counts as a keyboard. A key remapped to right Command there can therefore be the hotkey.
    - The requirement states both.
    - The spike confirmed that SharpHook reports the app's own posted Cmd+V with `IsEventSimulated=True` (M3).
  - **Wording for the microphone on macOS:**
    - "Error notifications" (`dictation`) points to *System Settings → Privacy & Security → Microphone*, opened through its `x-apple.systempreferences:` link, instead of Windows' microphone privacy settings.
    - "Microphone opened only while recording" (`audio-recording`) and "Dictation ends when the application exits" (`dictation`) say that the orange microphone indicator in the menu bar goes off, where they say that Windows no longer shows the app as using the microphone.
  - **Requirements to cover in its spec deltas** (D11):
    - `push-to-talk-hotkey`: Hotkey setting, Global detection, Simulated key events ignored, Reset on session switch, Missed release recovery
    - `audio-recording`: Default recording device, Microphone opened only while recording, Start completes when audio flows, Microphone access blocked, Microphone muted
    - `settings-window`: Hotkey editor
    - `dictation`: Error notifications, Dictation ends when the application exits
- **`add-macos-packaging`:**
  - An unsigned `.pkg`, like pisum-whisper (user decision):
    - `packaging/macos/build-app.sh` and `build-pkg.sh`
    - `pkgbuild` and `productbuild` without `--sign`, installing `Pisum Transcribe.app` into `/Applications`
    - a `postinstall`, run as root, that removes the quarantine from the app
    - A user approves the downloaded `.pkg` once with **Open Anyway**. The app itself is never assessed by Gatekeeper.
    - It is named `Pisum.Transcribe_<version>_osx-arm64.pkg`, and it is part of the lockstep release (D12).
  - The app is signed with the project's self-signed certificate (D9), so Accessibility and Microphone grants survive updates.
  - **M6 passed** for Accessibility across four rebuilds (Context). Still to check before the specs are written:
    - the Microphone grant
    - an update installed through the `.pkg` rather than rebuilt in place
    - The fallback stays ad hoc signing as in pisum-whisper, with the new grants after every update disclosed in the README.
  - **Proposed:** a Homebrew tap `mschnecke/homebrew-pisum-transcribe` with a cask that installs the `.pkg`. `release.yml` updates it through `repository_dispatch` after both installers are published, as pisum-whisper does. It saves the Open Anyway step on installs and updates.
  - No Developer ID or notarization, because there is no Apple Developer Program membership (user decision). The official Homebrew cask repository is closed to such apps: casks that fail Gatekeeper have been disabled since 2026-09-01.
  - **Upgrades behave as the MSI's do** (user decision). A plain `pkgbuild --root` package does none of this, and pisum-whisper's doesn't either:
    1. **Not relocatable.** A component plist (`pkgbuild --analyze`, edited, then `--component-plist`) sets `BundleIsRelocatable` = `false`. Otherwise Installer updates any other copy with the same bundle identifier instead of `/Applications`, such as the dev bundle in `bin/` (D9) or an app the user moved.
    2. **The running app ends first.** A `preinstall` sends the running app `SIGTERM`, which it handles as **Quit** (D5), and waits up to 6 s for it to exit, then sends `SIGKILL`. Otherwise files are replaced under the running process, and whatever it loads later, such as an assembly the first time the settings window opens, comes from the new version.
    3. **An interactive install starts the app.** The `postinstall` removes the quarantine, then opens the app as the console user (`launchctl asuser <uid> open -a "/Applications/Pisum Transcribe.app"`), unless `COMMAND_LINE_INSTALL` is set. The `installer` command line, and therefore Homebrew, sets it.
    4. **Older packages are refused.** `productbuild --distribution` with an `installation-check` script:
       - it reads the installed bundle's `CFBundleShortVersionString` and compares it without the pre-release suffix, as the MSI does
       - it refuses an older package with "A newer version of Pisum Transcribe is already installed."
       - a pre-release of the same version installs over the release and the other way round, like the MSI's same-version upgrades
    - The `packaging` spec's "Installing" and "Upgrading in place" requirements then describe both platforms with their mechanics (D11).
    - pisum-whisper could adopt 1–4 too. Row 1 matters most there, because its dev and release bundles share `net.pisum.whisper`.
  - **The MSI stays unsigned** (user decision). Microsoft treats a self-signed certificate like no signature: SmartScreen shows the same block, and the publisher stays unknown. SignPath Foundation's free signing for open source is the route if that changes.
- **`add-macos-login-item`, proposed:** `SMAppService.mainApp` (macOS 13+). It needs the bundle, which exists by then. pisum-whisper's LaunchAgent plist in `~/Library/LaunchAgents` is the fallback.
- **`add-macos-dictation`:**
  - The overlay is an Avalonia window. The spike's M2 passed, so no `NSPanel` is needed: the target app stayed frontmost, the paste landed, and the overlay showed over a full-screen TextEdit on its Space. The native settings are applied through the `NSWindow` handle before the first `Show`.
  - `pisum_overlay_configure(nswindow)` in the helper sets what Avalonia doesn't expose:
    - a floating `level`
    - `ignoresMouseEvents`, for click-through
    - `collectionBehavior` `.transient`, `.ignoresCycle`, `.fullScreenAuxiliary` and `.canJoinAllSpaces`
    - The overlay then stays out of Mission Control and shows over a full-screen target app, on that app's Space.
  - Placement follows the spec's rule on macOS: the bottom center of the `visibleFrame` (without the menu bar and the Dock) of the `NSScreen` that contains the target window. The window's frame comes from the Accessibility API.
  - The menu bar status icon is the shared glyph set (D4): a template at rest, red while recording and amber while transcribing. The `dictation` spec's "Tray icon states" holds unchanged.
  - **App Nap.** An agent app without visible windows can be napped: lower CPU priority and coalesced timers. That would slow the transcription after the release and make the elapsed-time counter stutter.
    - The helper therefore wraps each dictation, from the hotkey press until insertion ends, in `ProcessInfo.beginActivity(options: .userInitiated)`.
    - The model load and warm-up are wrapped the same way.
    - *Rejected:* `NSAppSleepDisabled` in Info.plist, which would keep the idle app awake all day to save a few seconds per dictation.
- **`add-metal-backend`:**
  - Metal with CPU fallback, and the rename of `BackendPreference.Vulkan` to a GPU value, with a migration of existing `settings.json` files.
  - **Requirements to cover in its spec deltas:**
    - `transcription`: the seven requirements that name Vulkan, which become "the GPU backend: Vulkan on Windows, Metal on macOS":
      - Warm-up before ready
      - Model stays loaded
      - Backend selection and fallback
      - Failures during transcription, which includes the return after running out of memory
      - Clean shutdown during transcription
      - Request cancelled by the caller
      - Reload on model or backend change
    - `dictation`: Cancel a transcription, Tray icon states ("Ready (Metal)")
    - `settings-window`: Backend settings, Apply changes without restart
  - **A benchmark checkpoint before `add-macos-dictation` fixes the defaults**, like the Windows checkpoint after the engine step (`docs/roadmap.md`). The target machine is the development Mac, a MacBook Air M4 with 16 GB. `TranscribeCppBenchmarkTests`, made backend-neutral by the rename, run there:
    - Metal against CPU for each installed catalog model, with the same target: a warm 10 s clip well under 3 s.
    - Q8_0 against Q4_K_M in accuracy.
    - The cancelled run on CPU.
    - The long-clip run on Metal, up to 399 s. With unified memory it may never run out of memory, and that decides whether the return-to-GPU logic after an out-of-memory error matters on macOS at all.
    - If Metal is slower than the CPU or unstable, or Q4_K_M matches Q8_0, the macOS default backend or model changes before `add-macos-dictation`.
    - The Vulkan-only environment variables of the benchmark (`GGML_VK_FORCE_MAX_*`) stay Windows-only.

## Risks / Trade-offs

- [A real logout carries no quit reason, unlike the spike's synthesized event] → D5's fallback through `NSWorkspace.willPowerOffNotification`. It's checked by hand with the dev bundle.
- [The unexplained startup abort seen once in the spike comes back] → The unhandled-exception logging names it. The manual checks include repeated launches through `open`.
- [`UNUserNotificationCenter` misbehaves in an ad-hoc-signed dev bundle] → The helper returns a status and the app logs. Notifications are checked with a local signing identity (D9), and again with the project's certificate in packaging.
- [Apple tightens Gatekeeper or `installer` for unsigned packages] → There's no fix without a membership. A build from source still works, because locally built apps aren't quarantined.
- [The project's certificate is lost] → Every user grants Accessibility and Microphone once more after the next update. The backup outside GitHub (D9) exists for this.
- [The certificate's private key leaks] → An app signed with it under the same bundle identifier would inherit users' grants. The key lives only in CI secrets and the backup, and never in the repository.
- [Building both frameworks on every host makes builds slower] → Accepted for the safety. Only managed code is compiled for the other platform.
- [`swiftc` versions differ between the Mac and CI] → Only stable, documented APIs are used, with a fixed deployment target. The Swift ABI is stable.
- [A permission grant is lost on every rebuild] → A local signing identity (D9), documented in `CLAUDE.md`.
- [Raising the ONNX Runtime pin raises the minimum macOS] → The note next to the pin (D1).
- [Two users logged in at once, through fast user switching, each run the app] → Intended: one instance per user, as Windows allows one per session.
- [The Windows job's time grows by compiling the macOS framework] → Accepted. It's managed code only.

## Migration Plan

1. After `move-windows-shell-to-avalonia` has shipped, and using its spike results: write the spec deltas (D11) and `tasks.md`.
2. Implement in order:
   - the second framework, with the moves and the registration of D14, and Windows still green
   - paths and the single-instance guard
   - the Swift helper with `pisum_abi_version`
   - the dev `.app`
   - the menu bar and **Quit**
   - the end of the session
   - notifications
   - the CI job
3. Check by hand on the Mac:
   - the menu bar icon in light and dark mode, and no Dock icon
   - **Quit** within 5 s, and logging out while the app runs, with no delay and the log entry
   - a second launch from Finder, from the terminal and through `open -n`, which leaves one icon
   - a kill, then a relaunch
   - the permission prompt and a notification
4. No release. The next macOS change builds on it, and the first macOS release comes with `add-macos-packaging`.

**Rollback:** revert the pull request. The Windows build only loses its second framework.
