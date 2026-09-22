## Why

The release zip from `add-packaging-ci` leaves installing to the user. They pick a folder, extract into it, find the exe, and later replace the folder by hand for every new version. The app has no Start Menu entry and no uninstall entry. If the user moves the folder, the autostart entry breaks until the app is started again. An MSI installer gives the app a fixed location, a Start Menu shortcut, an uninstall entry and in-place upgrades, without needing administrator rights. The installer is built with WiX v6, like `pisum-whisper`'s. The Velopack plan `add-installer-and-updates` bundled automatic updates with its installer. They're replaced by a notice about new versions, which `add-update-check` plans next.

## What Changes

- **Installer:** each release provides `Pisum.Transcribe_<version>_win-x64.msi`.
  - It installs for the current user, without administrator rights, into `%LOCALAPPDATA%\Programs\Pisum Transcribe\`. It adds a Start Menu shortcut and an entry in Windows' installed apps.
  - It contains everything the app needs, as the zip did: the .NET runtime and the Visual C++ runtime are in it, and nothing needs to be installed first.
  - A double-click installs it with Windows Installer's own progress window and then starts the app. Without a speech model, the app then opens its setup window. There are no install dialogs, no choice of folder and no desktop shortcut.
  - A silent install (`msiexec /qn`, as a package manager runs it) doesn't start the app.
- **Upgrading:** installing a newer MSI replaces the installed version in place. Settings, speech models, logs and the "Start with Windows" entry stay. A final release installs over its own pre-releases (`0.1.0` over `0.1.0-rc.2`), because Windows Installer compares only the numeric part of the version. A running app is closed through its normal shutdown before its files are replaced, and a double-click upgrade starts the new version when it finishes.
- **Uninstalling:** removes the program files, the Start Menu shortcut and the "Start with Windows" entry. It keeps `settings.json`, the logs and the downloaded speech models, so a reinstall keeps the user's setup.
- **BREAKING (release assets):** a release carries the MSI instead of `Pisum.Transcribe_<version>_win-x64.zip`. Anyone using the zip installs the MSI once and deletes the old folder. The app's first start from the installed location re-points an existing "Start with Windows" entry to itself, as it already does after a move.
- **Packaging:**
  - `packaging/windows/build-zip.ps1` becomes `build-msi.ps1`. It keeps all payload steps: publish, cleanup, license and notices, the local Visual C++ runtime and its guard. It then builds and validates the MSI instead of zipping.
  - The WiX tool and its Util extension are pinned exactly.
  - CI builds the MSI on every pull request and push to `main`, and the Release workflow publishes it.
- **Third-party notices:** the MSI contains WiX's custom action code, which starts the app after a double-click install and removes the startup entry on uninstall. WiX is MS-RL, so `THIRD-PARTY-NOTICES.md` gets a WiX Toolset section.
- **Documentation:**
  - The README's *Getting started* section installs the MSI.
  - `packaging/README.md`, `CLAUDE.md` and `docs/roadmap.md` describe the installer.
  - The roadmap names the update notice as the next change, and keeps WinGet as a deferred item.
- Not included:
  - a "new version available" notice: planned next as `add-update-check` (issue #2), which replaces the Velopack plan for updates. Updating in one click waits for code signing.
  - code signing: the MSI shows the same SmartScreen warning as the zip did.
  - a per-machine install, and WinGet and Chocolatey packages. WinGet comes later; a per-user MSI suits it.
  - deleting the speech models on uninstall.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `packaging`:
  - The zip requirement becomes an installer requirement: install per user without administrator rights, a Start Menu shortcut, and still no runtime to install first.
  - New requirements for upgrading in place and for uninstalling.
  - The requirements that name the archive (license notices, one version per release, publishing from a tag, only a tested build is released) now name the installer.

## Impact

- **New files:**
  - `packaging/windows/Pisum.Transcribe.wxs`, the MSI's WiX source.
  - `.config/dotnet-tools.json`, a local tool manifest that pins `wix`.
- **Changed files:**
  - `packaging/windows/build-zip.ps1` is renamed to `build-msi.ps1`.
  - `.github/workflows/ci.yml` and `release.yml`: the MSI instead of the zip.
  - `THIRD-PARTY-NOTICES.md`, `README.md`, `CLAUDE.md`, `docs/roadmap.md` and `packaging/README.md`.
- **No change to `src/` or `tests/` expected.** The uninstall cleanup is WiX custom actions, not app code. A running app is closed through the session-end handling it already has. The design's spike checks that. If the spike finds that the app needs a change, the tasks say so.
- **Build-time dependencies:** the `wix` .NET tool and `WixToolset.Util.wixext`, both 6.0.2. WiX's binaries come under its Open Source Maintenance Fee terms, whose fee applies only to users who make money with WiX; this project doesn't. No new package in the app.
- **User-visible:**
  - The release download becomes an MSI. It is about 75 MB, down from 94 MB for the zip.
  - The app gets a Start Menu entry and an uninstall entry in Windows Settings.
  - The zip release `v0.1.0-rc.1` stays as it is. The next release is the first MSI.
