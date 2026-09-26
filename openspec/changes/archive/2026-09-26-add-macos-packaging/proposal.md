## Why

Dictation works on the Mac since #20, but no Mac user can install it: a release carries only the MSI. The Mac app needs an installer that survives updates without asking for Accessibility and the microphone again, behaves like the MSI on upgrades, and ships in the same release as the MSI. This change brings the first lockstep release, 1.4.0, with 1.4.0-rc.1 before it.

Tracked in GitHub issue #22, with #21 ("Open at login") folded in. The decisions come from the section "Decided for later macOS changes" of the archived `add-macos-shell` design and from explore mode on 2026-09-26, which also ran spike M6 through a real `.pkg` update and a publish spike (see the design's Context).

## What Changes

- **An unsigned `.pkg` for Apple silicon:** `Pisum.Transcribe_<version>_osx-arm64.pkg`, built with `pkgbuild` and `productbuild`, installing `Pisum Transcribe.app` into `/Applications`.
  - It refuses Intel Macs and macOS 13 or older.
  - The bundle isn't relocatable, so Installer never updates another copy with the same bundle identifier.
  - A download is quarantined, so users approve the package once with **Open Anyway**. The package's `postinstall` then removes the app's quarantine.
- **Signing:** the app is signed with the project's own self-signed certificate "Pisum Transcribe", kept as CI secrets. Accessibility and microphone grants survive updates (spike M6). No Developer ID and no notarization.
- **Upgrades behave like the MSI's:**
  - a running app ends first through `SIGTERM`, as at **Quit**
  - an interactive installation starts the app
  - an older package is refused with a message
- **Uninstalling:** drag the app to the Trash. The README explains how to remove the data, the receipt and the grants too.
- **"Open at login"** in the settings window's general section on macOS, off by default, through `SMAppService`. It reflects changes made in *System Settings → General → Login Items*. This was #21.
- **Lockstep releases:** the release workflow builds the MSI and the `.pkg` in parallel, and publishes one release with both only when both builds and their tests pass. A guard stops the release when the bundle isn't signed with the project's certificate, has a library for another architecture or macOS version, or contains a Windows or Linux native file.
- **The update notice** names the menu bar on macOS instead of the tray menu.
- **Not included:**
  - the Homebrew tap (a later change, once a `.pkg` release exists)
  - signing or notarizing the MSI or the `.pkg`
  - an uninstall command

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `packaging`:
  - "macOS installer for Apple silicon" (new): the `.pkg`, its contents, `/Applications`, Open Anyway, the start after an interactive installation.
  - "Release signing on macOS" (new): the project's certificate, grants across updates, the guard.
  - "Upgrading in place": the macOS upgrade, with the running app ended by `SIGTERM` and an older package refused.
  - "Uninstalling": macOS by moving the app to the Trash.
  - "The installed application ships the license notices": the notices inside the app bundle.
  - "A release carries the source of its copyleft components": the source next to both installers.
  - "One version per release": the bundle's version.
  - "Publishing a release from a version tag": both installers, and nothing when either build fails.
  - "Only a tested build is released": the tests on both platforms.
- `settings-window`:
  - "Start with Windows": the macOS counterpart "Open at login", instead of no option on macOS.

## Impact

- **Depends on:** #20, merged in PR #35.
- **Code:**
  - `Pisum.Transcribe.csproj`:
    - the bundle target also runs after `Publish`, on `PublishDir`, and is skipped for the build output during a publish
    - ONNX Runtime's Windows ARM64 `onnxruntime.dll` is removed from the macOS output
  - `SettingsWindow/`: `IStartupRegistration` gets a "requires approval" state; `MacOS/MacLoginItem` implements it on `SMAppService`; the general section shows "Open at login" on macOS.
  - `Updates/UpdateCheckService`: the notice names the menu bar on macOS.
- **Swift helper:** `LoginItem.swift` (register, unregister, status). `pisum_abi_version` and `MacNativeLibrary.ExpectedAbiVersion` go from 4 to 5.
- **Packaging:** `packaging/macos/`, with `build-pkg.sh`, `Distribution.xml`, `preinstall`, `postinstall` and `assert-bundle.sh`, the guard.
- **Workflow:** `release.yml` gets the `build-macos` job with a temporary keychain from the secrets `MACOS_CERTIFICATE_P12` and `MACOS_CERTIFICATE_PASSWORD`, and `release` needs both builds. `ci.yml` doesn't change.
- **Docs:**
  - `README.md`: installing on the Mac, Open Anyway, uninstalling
  - `packaging/README.md`, `CLAUDE.md` (layout, ABI 5, the login item and the dev bundle), `docs/roadmap.md`
- **Releases:** 1.4.0-rc.1 first, then 1.4.0 over it, both checked by hand.
