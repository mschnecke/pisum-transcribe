The order follows the dependencies: the bundle first, then the package and its guard, then the workflow, "Open at login" and the notice, the docs, and at the end the releases checked by hand. Windows must stay green after every group: the MSI and `ci.yml` don't change.

## 1. The release bundle

- [x] 1.1 In `Pisum.Transcribe.csproj`, remove ONNX Runtime's `runtimes/win-arm64/native/` `None` items for the macOS framework (D2). Verify: a Debug build's dev bundle and a `dotnet publish -r osx-arm64` output hold no `onnxruntime.dll`; the MSI's publish on Windows (CI) is unchanged.
- [x] 1.2 Give `PisumAssembleAppBundle` an `AfterTargets="Publish"` entry on `$(PublishDir)`, and skip the `Build` entry while publishing (D1). The publish variant removes the flattened `LICENSE` from `Contents/MacOS/`, and puts the project's `LICENSE`, `THIRD-PARTY-NOTICES.md` and the texts from `packaging/third-party/` into `Contents/Resources/` under the MSI's names, and thins universal libraries to `arm64`, before it signs. Verify: a publish with the MSI's flags produces `Pisum Transcribe.app` in the publish folder, `codesign --verify --strict` passes, `Contents/Resources/` holds the license and notices, and `bin/` gets no new dev bundle from the publish; `dotnet build` still assembles the dev bundle as before.

## 2. The package

- [x] 2.1 Add `packaging/macos/assert-bundle.sh` (D3): the signature and the certificate leaf (default `a2eca9bd0a5157e33a43160ed01c502c6d86980b`, or `--leaf <sha1>` or `--adhoc`), `arm64` and `minos` ≤ 14.0 for every Mach-O, no PE file without a CLI header and no ELF file, and the bundle version. Verify: it passes on the bundle of 1.2 signed with a development identity (`--leaf`); it fails, each with a clear message, on a copy with a Windows DLL added, with an x86_64 library, with an ad hoc signature, and with a wrong version.
- [x] 2.2 Add `packaging/macos/Distribution.xml` (a template with `@VERSION@`), `preinstall` and `postinstall` (D4). Verify: `shellcheck` passes on both scripts; `preinstall`, run by hand against a running copy of the app in `/Applications`, ends it through `SIGTERM` (its log shows `TerminationRequest`) and leaves a dev bundle running; `postinstall` with `COMMAND_LINE_INSTALL=1` opens nothing.
- [x] 2.3 Add `packaging/macos/build-pkg.sh <version> [--identity <name>] [--leaf <sha1>|--adhoc]` (D1, D4): publish, assert, stage, component plist with `BundleIsRelocatable = false`, `pkgbuild` with only the two scripts, `productbuild` into `artifacts/Pisum.Transcribe_<version>_osx-arm64.pkg`. Verify: a local package signed with the development identity installs through Installer into `/Applications`, leaves the dev bundle in `bin/` untouched, starts the app after the interactive installation, and not after `sudo installer -pkg … -target /`; a package of a lower version is refused with the message; the same version with a pre-release suffix installs over the release.

## 3. The release workflow

- [ ] 3.1 In `release.yml`, rename `build` to `build-windows`, add `build-macos` on `macos-latest` (test, temporary keychain from the secrets, `build-pkg.sh`, keychain deleted `always()`, artifact `pkg`), and make `release` need both and publish the `.pkg` (D5, D6). Verify: `actionlint` passes; a dry run of `build-macos` on a branch through a temporary `workflow_dispatch` input that skips publishing, or a pre-release tag on a fork, produces a `.pkg` signed with the project's certificate (the guard passes with the default leaf).

## 4. Open at login

- [x] 4.1 Add `LoginItem.swift` with `pisum_login_item_status`, `_register` and `_unregister` on `SMAppService.mainApp`, raise `pisum_abi_version` and `MacNativeLibrary.ExpectedAbiVersion` to 5, and add the functions to `Hosting/MacOS/PisumMac` (D7). Verify: `MacNativeLibraryIntegrationTests` see ABI 5; a macOS `Integration` test reads a status from 0 to 3 in the test host without registering anything.
- [x] 4.2 Add `IStartupRegistration.RequiresApproval()` (false in `Windows/StartupRegistration`), add `SettingsWindow/MacOS/MacLoginItem`, and register it on macOS in `AddSettingsWindow()` (D7). Add the `SettingsWindow/MacOS/` entries to both `.csproj.DotSettings` files if they're missing. Verify: unit tests of `MacLoginItem` behind a fake of the helper's calls: each status, a failed register as `IOException`; the Windows tests still pass (CI).
- [x] 4.3 Show "Open at login" in the general section on macOS, with the approval hint, through one property and a platform label (D7, spec `settings-window` "Start with Windows"). Verify: `SettingsViewModelTests` and the headless `SettingsDialogTests` for macOS: the label, off by default, on after `IsEnabled`, off with the hint when approval is required, and `SetEnabled` on save; the Windows label and behavior unchanged.

## 5. The update notice

- [x] 5.1 Make `UpdateCheckService`'s notification say "the menu bar" on macOS and "the tray menu" on Windows (D8). Verify: `UpdateCheckServiceTests` assert the platform's text.

## 6. Docs and validation

- [x] 6.1 Update the docs. Verify: the texts match the code and the workflow.
  - `README.md`: installing on the Mac (Apple silicon, macOS 14, Open Anyway, the administrator's password), the first start with its permissions, and uninstalling (the Trash; complete removal with the data folders, `sudo pkgutil --forget io.github.mschnecke.pisum-transcribe` and `tccutil reset All io.github.mschnecke.pisum-transcribe`)
  - `packaging/README.md`: `build-pkg.sh`, the guard, the certificate with its fingerprint, the secrets and where the backup lives, and how the `.pkg` installs, upgrades and is removed
  - `CLAUDE.md`: the layout (`packaging/macos/`, `SettingsWindow/MacOS/`), ABI 5, the publish variant of the bundle target, and the login item's development trap
  - `docs/roadmap.md`: #21 folded into #22, the Homebrew tap as a later change, and this change's status
- [ ] 6.2 Run `openspec validate add-macos-packaging --strict`, `dotnet build Pisum.Transcribe.slnx` and `dotnet test Pisum.Transcribe.slnx` on the Mac, the test project compiled for Windows from a copy with `EnableWindowsTargeting`, and CI on Windows and macOS. Verify: all pass.

## 7. The releases, checked by hand

- [ ] 7.1 After the merge, release **1.4.0-rc.1** (D9). Verify:
  - the release is a pre-release with the MSI, the `.pkg` and the source archives
  - the downloaded `.pkg` needs Open Anyway once, installs, starts, and opens the setup window without a model
  - Accessibility and the microphone are granted, and a dictation into TextEdit works
  - the installed bundle's designated requirement names the project's leaf
  - a dev build stays untouched
  - "Open at login" works across a logout
  - the MSI installs on Windows as before
  - each result is noted on issue #22
- [ ] 7.2 Release **1.4.0** and install it over rc.1 while rc.1 runs (D9). Verify:
  - rc.1 ends through `SIGTERM`, and 1.4.0 starts after the installation
  - the grants and "Open at login" survive
  - reopening the rc.1 package is refused with the message
  - moving the app to the Trash removes its login item after a logout, and keeps the data folder
  - each result is noted on issue #22
