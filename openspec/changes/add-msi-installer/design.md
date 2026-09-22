## Context

See proposal.md, Why. This change builds on `add-packaging-ci` (archived 2026-09-22) and keeps its payload, its version handling and its two workflows. Only the last packaging step and the release asset change. The reference is `pisum-whisper`'s WiX v6 MSI (`W:\github-pisum-whisper\packaging\windows\`). That MSI is per-machine, harvests the publish folder with `<Files>`, and does nothing at uninstall.

Current state that the approach depends on:
- **`packaging/windows/build-zip.ps1`** does seven steps:
  1. publish into `packaging/windows/publish/Pisum Transcribe/`, self-contained, ReadyToRun, with `-p:ErrorOnDuplicatePublishOutputFiles=false`
  2. delete `*.lib`
  3. write the project's `LICENSE` over the one from the native package
  4. copy both notices files
  5. copy the Visual C++ runtime from the newest `Microsoft.VC14*.CRT` folder; the runner has VS 2026 with `Microsoft.VC145.CRT`
  6. run the import guard
  7. zip

  The payload is about 230 MB in 468 files. CI and `release.yml` call the script and upload `artifacts/*.zip`.
- **`release.yml`**: `build` and `release` carry explicit `if: ${{ !cancelled() && … == 'success' }}` conditions. A tag push skips `bump`, and without these conditions GitHub would skip the build and the release as well (found in the first rehearsal of `add-packaging-ci`).
- **`Directory.Build.props`** records `0.1.0-rc.1`. The zip pre-release `v0.1.0-rc.1` is published.
- **"Start with Windows"** (`StartupRegistration`) writes `"<exe path>"` to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `Pisum Transcribe`. Task Manager disables it through a value of the same name under `HKCU\…\Explorer\StartupApproved\Run`. At every start, the app re-points an existing `Run` value to its own exe (`StartupRegistration.cs:88`).
- **The data folder** is `%LOCALAPPDATA%\Pisum Transcribe\` (`AppPaths`), separate from the install folder `%LOCALAPPDATA%\Programs\Pisum Transcribe\`.
- **Session end:** `App.OnSessionEnding` routes Windows' end-session messages to `ShutdownCoordinator`, and the app ends as at **Exit** within 4.5 s (app-shell spec).
- **A spike on 2026-09-22 with WiX 6.0.2**, building the real payload in a scratch folder without installing it:
  - `wix build` doesn't run ICE validation, and `wix msi validate` does.
  - `Scope="perUser"` with the files under `LocalAppDataFolder\Programs` fails validation: 468 × ICE38 and 17 × ICE64 errors, plus 468 × ICE91 warnings. Every harvested file lies in the user profile with a file as its key path.
  - `Scope="perUserOrMachine"` with the files under `ProgramFiles64Folder` validates with only the expected ICE61 warning. Its properties are `ALLUSERS=2` and `MSIINSTALLPERUSER=1`, and summary Word Count is `2`, so the "no elevation needed" bit is not set.
  - `wix msi validate -sice ICE61 -wx` exits 0 on it. With `-wx`, any other warning fails.
  - The MSI is 75 MB, built in about 50 s after publishing.
  - `util:CloseApplication` compiles, but validation flags it with ICE105, because its deferred custom action runs without impersonation, which is invalid in a per-user package.
- **WiX binaries** come under the "Open Source Maintenance Fee" EULA (`OSMFEULA.txt` in the `wix` package). Its fee applies only to users who make money with the software, and this project doesn't. The source, including the custom action DLL embedded in the MSI, is MS-RL.

## Goals / Non-Goals

**Goals:**
- `wix msi validate` passes with no ICE error and no warning except the expected ICE61, checked on every build, including CI.
- The packaging stays one script that a person and CI run the same way (add-packaging-ci's goal).
- No change to `src/`. Install, upgrade and uninstall rely on the app's existing behavior: it re-points the autostart entry at start and handles session end.

**Non-Goals:**
- An install UI (dialogs, license page, folder choice, "launch now" checkbox) and a desktop shortcut.
- Restarting the app after an upgrade. Windows Installer restarts only apps registered with `RegisterApplicationRestart`.
- A per-machine install. The package is technically dual-purpose (D1), but only the per-user install is supported and tested.
- Removing the data folder on uninstall (proposal, user decision).

## Decisions

### D1: A dual-purpose package that installs per user

`Scope="perUserOrMachine"` gives `ALLUSERS=2` and `MSIINSTALLPERUSER=1`, so a double-click installs for the current user. The files go under `ProgramFiles64Folder`, which Windows Installer redirects to `%LOCALAPPDATA%\Programs\` for a per-user install. Microsoft calls this "single package authoring". Validation doesn't flag it, because the install folder isn't in the user profile at build time.

```
StandardDirectory ProgramFiles64Folder        -> %LOCALAPPDATA%\Programs\  (per user)
  Directory INSTALLFOLDER "Pisum Transcribe"
    <Files Include="$(var.PublishDir)\**" />    468 files, one component each
StandardDirectory ProgramMenuFolder           -> the user's Start Menu
  Component: Shortcut "Pisum Transcribe" -> [INSTALLFOLDER]Pisum.Transcribe.exe
             RegistryValue HKMU\Software\Pisum\Transcribe  (key path; HKCU per user)
```

- **Package:** Manufacturer "Michael Schnecke", as in `pisum-whisper`. A new fixed `UpgradeCode` that never changes. A new `ProductCode` for every build, which is the WiX default.
- **Installed-apps entry:** the icon from `src/Pisum.Transcribe/Tray/TrayIcon.ico`, `ARPNOMODIFY`, `ARPNOREPAIR`, and `ARPURLINFOABOUT` set to the GitHub repository.
- **No `WixUI`:** a double-click shows Windows Installer's own progress window.
- **The two source paths reach WiX as absolute `-d` values:** `PublishDir` and `IconFile`. That's `pisum-whisper`'s lesson: WiX resolves `SourceFile` and `<Files>` patterns against the current directory, not the `.wxs` file.

**To verify in the spike (tasks 1.x):** the "no elevation needed" bit is unset in a dual-purpose package, and only an installation shows whether a double-click installs without a UAC prompt. **Fallback:** `Scope="perUser"` with the files under `LocalAppDataFolder\Programs\Pisum Transcribe`. That validates only with `-sice ICE38 -sice ICE64 -sice ICE91`, which is acceptable in a non-roaming per-user folder: ICE38 and ICE64 are about roaming profiles. Either way the install folder and the specs stay the same.

*Rejected:*
- **`Scope="perUser"` as the first choice:** it needs three ICE suppressions, which would also hide real problems in those rules.
- **Generating one component per directory with HKCU key paths:** it satisfies ICE38 but brings back the harvesting step that `<Files>` removed.
- **Per-machine** (`pisum-whisper`'s choice) and **Velopack:** decided against in the proposal.

### D2: Version and upgrades

- **`ProductVersion`** is the numeric core of the release version: `build-msi.ps1` strips everything from the first `-`, as `pisum-whisper`'s does. The file name keeps the full version.
- **`<MajorUpgrade AllowSameVersionUpgrades="yes" DowngradeErrorMessage="A newer version of Pisum Transcribe is already installed." />`**
  - A same-version upgrade is how `0.1.0` replaces `0.1.0-rc.2`, because both are `0.1.0` to Windows Installer.
  - The price is that `0.1.0-rc.3` would also replace an installed `0.1.0`. That's acceptable: pre-releases are for rehearsing.
  - ICE61 warns about this, and `-sice ICE61` suppresses exactly that one warning.
- **The default schedule (`afterInstallValidate`)** removes the old product completely before it installs the new one. So which component holds which file doesn't have to match between versions, and `<Files>` may harvest a different payload each time.
- **Data survives an upgrade** because the MSI never owns the data folder or the `Run` value. The install folder stays the same, so the `Run` value keeps pointing at the right exe.

### D3: Uninstall removes the startup entry with two quiet custom actions

Two deferred, impersonated `WixQuietExec64` custom actions from WiX Util. Each runs `"[System64Folder]reg.exe" delete "<key>" /v "Pisum Transcribe" /f`: one for `…\CurrentVersion\Run`, one for `…\Explorer\StartupApproved\Run`.
- **They run only on a real uninstall:** the condition is `REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE`, so an upgrade keeps "Start with Windows".
- **`Return="ignore"`:** `reg.exe` exits with 1 when the value doesn't exist, and a failed cleanup must never block an uninstall.
- **`WixQuietExec64`** runs `reg.exe` without a console window flashing.

*Rejected:*
- **MSI's `RemoveRegistry` table (`<RemoveRegistryValue>`):** it acts when its component is installed, not when it's removed.
- **`<RemoveRegistryKey Action="removeOnUninstall">`:** it would delete the whole `Run` key, including other apps' entries.
- **Making the `Run` value an MSI-owned registry value:** the MSI would then write it at install and turn on autostart.
- **A `--uninstall` switch in the app:** app code and a start of the app inside an uninstall, for two registry deletes that `reg.exe` does.

### D4: A running app is closed by Restart Manager, through its session-end handling

Windows Installer's Restart Manager, on by default, finds `Pisum.Transcribe.exe` in use during an upgrade or uninstall.
- **Interactive install:** without authored UI, Windows Installer shows its own "files in use" prompt.
- **Closing:** Restart Manager then sends the app `WM_QUERYENDSESSION` and `WM_ENDSESSION` with `ENDSESSION_CLOSEAPP`. WPF turns that into `SessionEnding`, and `App.OnSessionEnding` ends the app through `ShutdownCoordinator`, as at sign-out. That's within 4.5 s, and no reboot is needed.
- **Silent install (`/qn`):** Restart Manager closes the app without asking.

*Rejected:*
- **`util:CloseApplication`:** ICE105 (Context), and it would duplicate what Restart Manager does.
- **Terminating the process:** that skips the shutdown, so the tray icon lingers and a dictation in progress is lost.

**To verify in the spike:**
- the prompt appears on a double-click upgrade
- the app's log shows the end-of-session shutdown
- the upgrade finishes without asking for a reboot

If Restart Manager can't close the app, because it finds no window to send the message to, the fallback is a change in `src/`, which the tasks then name. The spec requires only the outcome.

### D5: `build-zip.ps1` becomes `build-msi.ps1`

It's renamed with `git mv`, so its history follows. Steps 1 to 6 stay as they are. Step 7 becomes:

```
dotnet tool restore                                   # wix, pinned in .config/dotnet-tools.json
dotnet wix extension add -g WixToolset.Util.wixext/<same version as wix>
dotnet wix build packaging/windows/Pisum.Transcribe.wxs -arch x64 -ext WixToolset.Util.wixext
         -d Version=<numeric core> -d PublishDir=<abs> -d IconFile=<abs>
         -out artifacts/Pisum.Transcribe_<version>_win-x64.msi
dotnet wix msi validate -sice ICE61 -wx artifacts/Pisum.Transcribe_<version>_win-x64.msi
```

- **Pins:**
  - `wix` is pinned exactly in a local tool manifest, not installed globally with `6.*` as in `pisum-whisper`.
  - The script reads the extension's version from that manifest, so the two can't drift apart.
  - `-g` puts the extension in WiX's per-user cache, not in a folder in the repository.
- **Exit codes:** the script checks the exit code of every `dotnet` call, as it does today.
- **Output:** it prints the payload size, the MSI size and the `ProductVersion` next to the full version.

### D6: Workflows

- **`ci.yml`:** "Build the zip" becomes "Build the MSI". The upload becomes `artifacts/*.msi` with the artifact name `msi`, 7-day retention and `if-no-files-found: error`. So every pull request builds and validates the MSI.
- **`release.yml`:**
  - `build` runs `build-msi.ps1` and uploads the MSI.
  - `release` publishes exactly `artifacts/Pisum.Transcribe_<version>_win-x64.msi`.
  - The explicit `if:` conditions, `fail_on_unmatched_files` and `generate_release_notes` stay as they are.

### D7: Notices

`THIRD-PARTY-NOTICES.md` gets a **WiX Toolset** section:
- the component: the custom action DLL `Wix4UtilCA_X64`, which is embedded in the MSI and runs only while the package is uninstalled (D3)
- the source: `wixtoolset/wix`, tag `v6.0.2`
- the license: the MS-RL text

The spec's notices requirement now covers code that runs only during install or uninstall. The WiX tool itself isn't shipped. The notices file ships in the install folder, as before.

### D8: Documentation

| File | Change |
|---|---|
| `README.md` | *Getting started*: download the MSI, open it, and at the SmartScreen prompt choose **More info** → **Run anyway**. The app is in the Start Menu. Upgrade by installing a newer MSI. Uninstall from Windows Settings → Apps; that keeps settings, logs and models, which the user deletes by hand if wanted. Zip users install the MSI once and delete their old folder. The sizes come from the first MSI build. |
| `packaging/README.md` | The MSI build and its validation, the dual-purpose package (D1), versions and same-version upgrades (D2), the uninstall cleanup (D3), the pins (D5), and proving the MSI on a clean machine and without administrator rights. |
| `CLAUDE.md` | *Layout* and *Commands*: `build-msi.ps1` and `Pisum.Transcribe.wxs` instead of `build-zip.ps1`. |
| `docs/roadmap.md` | `add-msi-installer` as a step. *Deferred*: automatic updates, noting that `add-installer-and-updates` (#2) records the Velopack approach, plus code signing and WinGet/Chocolatey. |

## Risks / Trade-offs

- [A dual-purpose package asks for elevation on a double-click] → The spike installs it first (D1). The fallback `Scope="perUser"` keeps the install folder and the specs.
- [Restart Manager can't close the tray app, and the upgrade asks for a reboot] → The spike checks it (D4). The fallback is a small change in the app, named in the tasks.
- [Same-version upgrades let a later pre-release replace a final release with the same numeric version] → Accepted (D2). Pre-releases are rehearsals and aren't offered as the latest release.
- [SmartScreen warns about the unsigned MSI as it did about the zip] → Documented. Signing stays a separate change.
- [A user has both the zip and the MSI] → The `Run` value follows whichever copy started last, and both use the same data folder. The README says to delete the zip folder after installing.
- [The WiX tool and the Util extension drift apart] → One pin in the tool manifest, which the script reads for the extension (D5).
- [WiX's Open Source Maintenance Fee terms change, or the project starts making money] → The fee applies only to revenue-generating users today. The terms are in the pinned package's `OSMFEULA.txt`, to be re-read when the pin changes.
- [ICE validation takes time in every CI run] → Accepted: a few minutes on a pull request, in exchange for catching a broken package before a release.

## Migration Plan

1. **Spike** (tasks 1.x), on the development machine or in Windows Sandbox:
   - Install a first `Pisum.Transcribe.wxs` MSI by double-click, and look for a UAC prompt.
   - Check the install folder, the Start Menu shortcut and the installed-apps entry.
   - Upgrade while the app runs, uninstall while it runs, and check the startup entry.
   - Settle D1's and D4's fallbacks there.
2. Implement the script, the workflows, the notices and the docs. A pull request's CI builds and validates the MSI.
3. Rehearse with a tag on the branch: an exact `0.1.0-rc.2` publishes a pre-release with the MSI. Prove it on a clean machine, then delete the rehearsal.
4. After the merge, start **Release** by hand with `0.1.0-rc.2`. The first final release, `0.1.0`, is a separate step.

**Rollback:** revert the pull request. `build-zip.ps1` and the zip steps come back with it. Installed MSIs stay uninstallable from Windows Settings.
