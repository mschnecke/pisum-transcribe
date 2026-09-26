## Context

See proposal.md for the motivation. The Mac build runs and dictates since #20, but a release carries only the MSI (`release.yml`: `version` → `build` on `windows-latest` → `release`). The shell design (`add-macos-shell`, D9 and D12, and "Decided for later macOS changes") already fixed the direction: an unsigned `.pkg` like pisum-whisper's, the app signed with the project's own self-signed certificate, upgrades like the MSI's, and lockstep releases.

**What exists:**
- `Pisum.Transcribe.csproj` assembles and signs the dev bundle after `Build` (`PisumAssembleAppBundle`):
  - the build output goes into `Contents/MacOS/`
  - `Info.plist` comes from `MacOS/Info.plist` with `@VERSION@` and `@MINIMUM_SYSTEM_VERSION@`
  - `AppIcon.icns` goes into `Contents/Resources/`
  - it signs inside-out with `PisumCodesignIdentity`, `-` (ad hoc) by default
- `packaging/windows/build-msi.ps1` publishes self-contained with ReadyToRun, with `-p:ErrorOnDuplicatePublishOutputFiles=false` and `-p:PublishDocumentationFile=false`. It overwrites the flattened `LICENSE` with the project's own, adds `THIRD-PARTY-NOTICES.md` and the texts from `packaging/third-party/`, and runs `assert-native-dependencies.ps1` as its guard.
- `IStartupRegistration` has only a Windows implementation (`Windows/StartupRegistration`, the `Run` key). The settings window hides "Start with Windows" without one, so on macOS.
- `UpdateCheckService`'s notification says "Choose it in the tray menu to open the release page." on both platforms.
- The Swift helper is at ABI 4.

**Explore mode on 2026-09-26:**
- **#21 is folded in.** It was closed as completed without being built, and the user wants "Open at login" in 1.4.0. The Homebrew tap waits for a later change.
- **The project's certificate** "Pisum Transcribe" exists:
  - an RSA 3072-bit, self-signed code-signing certificate, valid for 20 years
  - SHA-1 fingerprint `A2:EC:A9:BD:0A:51:57:E3:3A:43:16:0E:D0:1C:50:2C:6D:86:98:0B`, so every release's designated requirement is `identifier "io.github.mschnecke.pisum-transcribe" and certificate leaf = H"a2eca9bd0a5157e33a43160ed01c502c6d86980b"`
  - stored as the repository secrets `MACOS_CERTIFICATE_P12` (base64) and `MACOS_CERTIFICATE_PASSWORD`, set on 2026-09-26, with a backup outside GitHub kept by the maintainer
  - not in any login keychain
- **Spike M6 through a real `.pkg` update,** in the scratchpad. Two builds were signed with the development certificate, with different CDHashes and the same designated requirement, and wrapped by `pkgbuild` with a component plist setting `BundleIsRelocatable = false`:
  - After `tccutil reset`, spike.1 was installed through Installer into `/Applications`, and granted Accessibility (with the relaunch) and the microphone fresh.
  - spike.2 installed over it. The app started with the hook running right away, and recorded and transcribed without a prompt. **Both grants survived the update.**
  - Installer wrote to `/Applications`, not to the dev bundle in `bin/`, which has the same bundle identifier. **"Not relocatable" works.**
  - The locally built packages weren't quarantined, so Gatekeeper's Open Anyway wasn't part of the spike.
- **A publish spike:** `dotnet publish -c Release -f net10.0 -r osx-arm64 --self-contained -p:PublishReadyToRun=true`:
  - Without the MSI's flags it fails with `NETSDK1152`: the native package of TranscribeCppSharp has three `LICENSE` files under `licenses/` that NuGet flattens onto one path.
  - With them it succeeds: 265 files, 187 MB, including `libPisumMac.dylib` (the Swift target runs during publish too), `libcoreclr.dylib` and every native library, Metal included.
  - The output holds **`onnxruntime.dll`, a native Windows ARM64 DLL** (16 MB). `Microsoft.ML.OnnxRuntime.props` copies `runtimes/win-arm64/native/onnxruntime.dll` whenever `PlatformTarget` is `ARM64`, whatever the operating system, so every `osx-arm64` build carries it, the dev bundle included.
  - The dev bundle target ran during the publish too, on the build output in `bin/`.

## Goals / Non-Goals

**Goals:**
- One command, `packaging/macos/build-pkg.sh <version>`, turns a checkout into the release package, on a developer's Mac and on the runner, as `build-msi.ps1` does for Windows.
- The dev bundle and the release bundle come from one MSBuild target, so their plist, icon and signing can't drift apart.
- A release never ships a bundle that would lose users' grants or crash on their Mac: the guard runs before anything is published.

**Non-Goals:**
- A Developer ID signature, notarization, or a signed `.pkg`. There is no Apple Developer Program membership.
- The Homebrew tap (a later change).
- An uninstall command or menu item. The Trash, documented, is the Mac's convention.
- Intel Macs, and macOS 13 or older (D1 of the shell design).
- Any change to the MSI or to `ci.yml`.

## Decisions

### D1: The release bundle comes from the csproj target, after `Publish`

- `PisumAssembleAppBundle` gets a second entry point, `AfterTargets="Publish"`, that assembles `$(PublishDir)Pisum Transcribe.app` from the publish output. The `AfterTargets="Build"` entry is skipped while publishing (`'$(_IsPublishing)' != 'true'`), so a publish no longer assembles and signs the dev bundle in `bin/` as a side effect.
- Only the publish variant also:
  - removes the flattened `LICENSE` of the native package from `Contents/MacOS/`
  - puts the project's `LICENSE`, `THIRD-PARTY-NOTICES.md` and the texts from `packaging/third-party/` into `Contents/Resources/`, before signing, so the seal covers them
  - thins universal libraries to their `arm64` slice (`lipo -thin arm64`), before signing. SkiaSharp, HarfBuzzSharp and Avalonia.Native ship `x86_64` and `arm64` in one file, which the guard (D3) would refuse; thinning saves about 9 MB (user decision during apply).
- The notices get the same file names as in the MSI (`ThirdPartyNotices-DotNet.txt` and so on). The ONNX Runtime notice is one of them.
- Signing stays inside-out with `PisumCodesignIdentity`. `build-pkg.sh` passes `-p:PisumCodesignIdentity="Pisum Transcribe"` on the runner. Locally, a developer can pass their development identity to build a test package.
- **The publish call** in `build-pkg.sh`: `dotnet publish src/Pisum.Transcribe -c Release -f net10.0 -r osx-arm64 --self-contained true -p:PublishReadyToRun=true -p:PublishDocumentationFile=false -p:ErrorOnDuplicatePublishOutputFiles=false -p:Version=<version>`, the same flags as the MSI (D2 of the Windows packaging design), so both installers carry the same kind of build.

*Rejected:* a separate `build-app.sh` like pisum-whisper's. It would be a second implementation of copying, the plist and signing, next to a target that already does them (user decision).

### D2: No Windows ARM64 DLL in Mac builds

For the macOS framework, the csproj removes the `None` items that ONNX Runtime's props add from `runtimes/win-arm64/native/` (`onnxruntime.dll`, `onnxruntime_providers_shared.dll`). It matches them by that path segment, not by the package version, so the pin can change. The rule applies to the dev bundle too.

*Rejected:* deleting the file in `build-pkg.sh`, the way `build-msi.ps1` deletes `*.lib`. The dev bundle would keep carrying 16 MB of Windows code, and the csproj is where the stray item comes from.

### D3: The guard, `packaging/macos/assert-bundle.sh`

It runs in `build-pkg.sh` after the publish, and fails the build, and with it the release, when:
- **the signature** doesn't verify (`codesign --verify --strict`), or the designated requirement (`codesign -d -r-`) doesn't contain `certificate leaf = H"a2eca9bd0a5157e33a43160ed01c502c6d86980b"`. A swapped secret would otherwise cost every user their grants with the next update. The fingerprint is a parameter with that default, so a developer can check a test package signed with their own identity (`--leaf <sha1>` or `--adhoc`).
- **a Mach-O file** isn't `arm64` only (`lipo -archs`), after D1's thinning, or its `minos` (`vtool -show-build`) is newer than `14.0`. That catches an ONNX Runtime update that raises its minimum (D1 of the shell design).
- **a native file of another platform** is in the bundle: PE executables that aren't .NET assemblies, or ELF files. This would have caught D2's stray DLL. .NET assemblies are PE files too; the check tells them apart by their CLI header, as `file` reports it.
- **the bundle's version** (`CFBundleShortVersionString`) differs from the version the script was given.

### D4: The package: component, scripts, distribution

`build-pkg.sh` builds:
1. **The component,** with `pkgbuild --root <stage> --component-plist <plist> --install-location /Applications --identifier io.github.mschnecke.pisum-transcribe --version <version> --scripts <scripts>`. The component plist comes from `pkgbuild --analyze` with `BundleIsRelocatable = false` (spike M6), and also `BundleHasStrictIdentifier = true` and `BundleOverwriteAction = upgrade`. `BundleIsVersionChecked` is `false`: with it, Installer would silently skip a bundle it considers older by its own comparison, which doesn't know pre-release suffixes, so the `installation-check` below, with its message, is the only version rule.
2. **The product,** with `productbuild --distribution packaging/macos/Distribution.xml --package-path <dir> Pisum.Transcribe_<version>_osx-arm64.pkg`, unsigned.

**`Distribution.xml`:**
- `<options hostArchitectures="arm64" customize="never" require-scripts="false"/>` and `<domains enable_localSystem="true"/>`, so no "install for me only" and no choice of location
- `<allowed-os-versions><os-version min="14.0"/></allowed-os-versions>`, with the message "Pisum Transcribe needs macOS 14 or later."
- `<installation-check script="checkVersion()"/>`: JavaScript that reads `system.files.bundleAtPath('/Applications/Pisum Transcribe.app')`. When the bundle exists, it compares its `CFBundleShortVersionString` with the package's version, both as `major.minor.patch` without the pre-release suffix. It refuses a lower version with "A newer version of Pisum Transcribe is already installed." (`my.result.type = 'Fatal'`). Equal versions pass, so a release installs over its release candidates and the other way round, like the MSI's same-version rule.
- The package's version gets into the script when `build-pkg.sh` fills `@VERSION@` in a copy of the template.

**`preinstall`,** run as root:
- It finds every process whose executable is `/Applications/Pisum Transcribe.app/Contents/MacOS/Pisum.Transcribe` (`pgrep -f` anchored on that path), across all users. The dev bundle and pisum-whisper are untouched.
- It sends them `SIGTERM`, which the app handles as `ShutdownReason.TerminationRequest`, the same as **Quit**, within its 5 s budget.
- It waits up to 6 s for them to exit, then sends `SIGKILL`. It always exits 0: a process that won't die doesn't stop the installation.

**`postinstall`,** run as root:
- `xattr -dr com.apple.quarantine "/Applications/Pisum Transcribe.app"`, as pisum-whisper does.
- Unless `COMMAND_LINE_INSTALL` is set, as `installer` and Homebrew set it, it opens the app for the console user: `stat -f%Su /dev/console`, skipped for `root` and `loginwindow`, and `launchctl asuser <uid> sudo -u <user> open "/Applications/Pisum Transcribe.app"`.
- Instances of other users that `preinstall` ended stay ended until those users open the app again. The MSI has no counterpart, because it installs per user.
- It always exits 0.

The scripts directory holds only `preinstall` and `postinstall`. `build-pkg.sh` copies them into a temporary directory, so nothing else from `packaging/macos/` ends up inside the package, a lesson from pisum-whisper.

### D5: Signing on the runner

The `build-macos` job:
1. creates a temporary keychain with a random password (`security create-keychain`), sets it unlocked with no timeout, and puts it first in the search list
2. imports the `.p12` from `MACOS_CERTIFICATE_P12` and `MACOS_CERTIFICATE_PASSWORD` with `-T /usr/bin/codesign`, and sets the key's partition list (`security set-key-partition-list -S apple-tool:,apple:,codesign:`). No trust setting is needed (spike M6 of the shell design).
3. runs `build-pkg.sh` with the identity "Pisum Transcribe"
4. deletes the keychain in a step that runs `always()`

The secrets reach only this job. `ci.yml` keeps signing ad hoc and needs no secret, so pull requests from forks build as before.

### D6: Lockstep in `release.yml`

- Today's `build` becomes `build-windows`, unchanged apart from its name and its artifact.
- A new `build-macos` on `macos-latest` checks out the tag, sets up .NET from `global.json`, runs `dotnet test Pisum.Transcribe.slnx -c Release` (the Mac's default test run), then D5, then `build-pkg.sh <version>`, and uploads `artifacts/*.pkg` as the artifact `pkg`.
- `release` `needs: [version, build-windows, build-macos]` and runs only when both builds succeeded (D12 of the shell design). It downloads both artifacts and publishes the `.pkg` next to the MSI and the source archives. `fail_on_unmatched_files` makes a missing installer fail the run.
- Pre-releases stay as they are: a version with a suffix publishes a pre-release, which `UpdateCheckService` ignores.

### D7: "Open at login" through `SMAppService`

- **`IStartupRegistration`** gets `bool RequiresApproval()`, which is always `false` on Windows. Its documentation becomes platform-neutral.
- **`SettingsWindow/MacOS/MacLoginItem`** implements it on the helper:
  - `IsEnabled` is `true` only for the status "enabled"
  - `RequiresApproval` is `true` for "requires approval"
  - `SetEnabled(true)` registers and `SetEnabled(false)` unregisters. Failures come back as an `IOException` with the helper's status, which the settings window already reports for the registry.
- **`LoginItem.swift`** has three functions on `SMAppService.mainApp`: `pisum_login_item_status` (0 not registered, 1 enabled, 2 requires approval, 3 not found), `pisum_login_item_register` and `pisum_login_item_unregister`, each returning a status. It's called on the UI thread, where the settings window runs. The ABI goes from 4 to 5.
- **The general section:**
  - it shows "Start with Windows" on Windows and "Open at login" on macOS, with one property and a platform label
  - with "requires approval", the option reads as off, and a hint says "Allow Pisum Transcribe in System Settings → General → Login Items."
  - `SettingsViewModel` already applies and re-reads the state when the window opens, so a change in System Settings shows the next time.
- **Uninstalling:** an app moved to the Trash isn't started at login any more, but macOS keeps its login item registered, pointing into the Trash, and still after the Trash is emptied (found in task 7.2 on macOS 27; the design first assumed macOS drops it). The README therefore says to turn off "Open at login" first.
- **The development trap:** `SMAppService.mainApp` registers the bundle that runs. Turning the option on in the dev bundle from `bin/` makes macOS open the dev build at login. `CLAUDE.md` says so.

*Rejected:* a LaunchAgent plist in `~/Library/LaunchAgents` (pisum-whisper's way). It would be a file to write, keep pointing at the right bundle, and remove on uninstall, while macOS 13 and later manages `SMAppService` login items itself and shows them in Login Items.

### D8: The update notice on macOS

`UpdateCheckService`'s notification text names the platform's place: "Choose it in the tray menu to open the release page." on Windows, and "Choose it in the menu bar to open the release page." on macOS, as a constant with `#if WINDOWS`. The `app-updates` spec doesn't pin the text, so no spec changes.

### D9: The first releases

- **1.4.0-rc.1** is the first lockstep release, a pre-release. It's checked by hand: download the `.pkg` from the Releases page, so it's quarantined; Open Anyway on macOS 27; install; the grants; a dictation. It's installed next to a dev build to check that the development copy stays untouched. The MSI of the same release is checked on Windows.
- **1.4.0** follows and is installed over rc.1 while it runs. That checks the same-version rule, the `preinstall`'s `SIGTERM`, the start after the installation, and that the grants and "Open at login" survive.
- The release notes say that the Mac app needs Apple silicon and macOS 14, and that the first installation needs Open Anyway.

## Risks / Trade-offs

- **[Apple changes Open Anyway for unsigned packages in a later macOS]** → There's no fix without a membership. A build from source still works. The rc.1 check shows the state on macOS 27, and the README describes the steps with their System Settings path.
- **[The certificate is lost or leaks]** → Losing it means every user grants Accessibility and the microphone once more after the next update. The backup outside GitHub exists for that. A leak would let an app with the same bundle identifier inherit the grants. The key exists only in the secrets and the backup, never in a keychain in daily use.
- **[`system.files.bundleAtPath` or `installation-check` behave differently than documented]** → The rc.1 to 1.4.0 upgrade is checked by hand, and a package of an older version (`1.3.9-dev.1` over `1.4.0`) was refused with the message in task 2.3. rc.1 over 1.4.0 isn't a downgrade under the same-version rule and passes the check (found during task 7.2, where the plan had wrongly expected a refusal).
- **[`SIGTERM` reaches an app that hangs]** → `SIGKILL` after 6 s. The app's own watchdog ends it after 4.5 s anyway.
- **[A login item survives the move to the Trash on some macOS]** → It's checked by hand. The README's complete removal also covers Login Items.
- **[ReadyToRun or self-contained output doesn't run when signed]** → The publish spike showed a complete output. The rc.1 check runs the real release bundle, and the guard verifies the signature before packaging.
- **[A lockstep failure holds back a Windows fix]** → Accepted in D12 of the shell design.

## Migration Plan

- There are no data or settings changes. The first macOS installation starts from an empty data folder, or from the one a dev build left.
- A Mac user who ran a dev build signed with another identity grants the permissions once more for the release. Different signatures are different apps to macOS.
- **Rollback:** delete the release and its tag. The MSI of a failed release is never published alone (D6).
