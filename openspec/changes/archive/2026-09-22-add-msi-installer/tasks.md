## 1. The package, proved on a real install first (design D1–D4)

- [x] 1.1 Pin WiX in a local tool manifest: `dotnet new tool-manifest`, then `dotnet tool install wix --version 6.0.2`, which writes `.config/dotnet-tools.json`. Verify: in a fresh shell, `dotnet tool restore` and then `dotnet wix --version` print `6.0.2`.
- [x] 1.2 Write `packaging/windows/Pisum.Transcribe.wxs` as design D1–D3 describe:
  - `Scope="perUserOrMachine"`, with `INSTALLFOLDER` "Pisum Transcribe" under `ProgramFiles64Folder`, harvested with `<Files Include="$(var.PublishDir)\**" />`
  - the Start Menu shortcut, with an `HKCU\Software\Pisum\Transcribe` key path (`HKMU` fails ICE57 next to the per-user shortcut)
  - the installed-apps icon and properties
  - `<MajorUpgrade AllowSameVersionUpgrades="yes" …>` with the downgrade message and the default schedule, and `<MediaTemplate EmbedCab="yes" />`. A comment next to `<MajorUpgrade>` explains why the schedule must stay `afterInstallValidate`: the app's file versions don't carry the pre-release suffix (design D2).
  - the two `WixQuietExec64` custom actions for the `Run` and `StartupApproved\Run` values, with `REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE` and `Return="ignore"`
  - the `WixShellExec` custom action that starts the app after `InstallFinalize`, with `WixShellExecTarget` set to `[INSTALLFOLDER]Pisum.Transcribe.exe` and the condition `UILevel = 5 AND NOT Installed AND NOT REMOVE` (design D9)
  - a header comment on why `PublishDir` and `IconFile` are absolute `-d` values (design D1)

  Verify: on a publish folder from `./packaging/windows/build-zip.ps1 -Version 0.1.0-dev.1`, `dotnet wix build` with `-arch x64 -ext WixToolset.Util.wixext -d Version=0.1.0` and the two absolute paths succeeds, and `dotnet wix msi validate -sice ICE61 -wx` on the result exits 0.
- [x] 1.3 Install spike (design D1). First write down whether "Start with Windows" is on: the uninstall in 1.4 removes the real `Run` value. Open the MSI from 1.2 by double-click as a user without an elevated session, with logging (`msiexec /i <msi> /l*v install.log` gives the same install). Verify:
  - no UAC prompt appears
  - the files are in `%LOCALAPPDATA%\Programs\Pisum Transcribe\`
  - the app starts when the install finishes, shows its tray icon, and runs without elevation: Task Manager's Details tab shows "Elevated: No" (design D9)
  - the Start Menu has "Pisum Transcribe", which also starts the app
  - Windows Settings → Apps shows "Pisum Transcribe" with version `0.1.0` and the icon

  If a UAC prompt appears, switch to D1's fallback: `Scope="perUser"`, `LocalAppDataFolder\Programs\Pisum Transcribe`, validation with `-sice ICE38 -sice ICE64 -sice ICE91` as well. Write the switch into design D1, and repeat this task. If the app doesn't start, or starts elevated or as another user, stop: D9's fallback drops the start and changes the spec, so it needs your decision first.

  Spike on 2026-09-22: UAC is off on the development machine (`EnableLUA` is 0), so every process there, Explorer included, runs with the full administrator token, and the first and third checks can't be proven on it. Everything else passed: a per-user install (`AssignmentType` 0), the files, the app starting after the install as the signed-in user with Explorer's token, the shortcut, and the installed-apps entry at `0.1.0` with its icon. By decision, the UAC checks move to 6.2, on a machine with UAC on. This task is done when they pass there. They passed in 6.2: no UAC prompt, and the started app ran with "Elevated: No". D1 stays as designed.
- [x] 1.4 Upgrade and uninstall spike (design D2–D4). In the installed app, turn on "Start with Windows". Build two more MSIs from the same payload: one with `-d Version=0.1.1` and one with `-d Version=0.0.9`. Verify:
  - **Upgrade while running:** installing `0.1.1` while the app runs shows a "files in use" prompt; record its text for the README. Closing applications ends the app, and the log shows the end-of-session shutdown. No reboot is asked for. The new version starts when the upgrade finishes, and its log shows the version. Settings → Apps shows one entry, at `0.1.1`. "Start with Windows" is still on and points at the same exe.
  - **Same-version upgrade:** reinstalling a rebuilt `0.1.1` MSI replaces the installed copy.
  - **Downgrade refused:** opening the `0.0.9` MSI shows "A newer version of Pisum Transcribe is already installed.", and nothing changes.
  - **Uninstall while running:** uninstalling from Settings → Apps ends the app. The `Run` and `StartupApproved\Run` values named `Pisum Transcribe` are gone (`reg query`). The install folder and the Start Menu shortcut are gone. `%LOCALAPPDATA%\Pisum Transcribe\` still has `settings.json`, `logs\` and `models\`.
  - **Unattended install:** `msiexec /i <0.1.1 msi> /qn` installs without starting the app, and `Get-Process Pisum.Transcribe` finds nothing. Then `msiexec /x <0.1.1 msi> /qn` removes it again.

  If Restart Manager can't close the app, stop and name the change in `src/` that design D4's fallback needs before going on. Afterwards, restore "Start with Windows" as recorded in 1.3.

## 2. Build script (design D5)

- [x] 2.1 `git mv packaging/windows/build-zip.ps1 packaging/windows/build-msi.ps1`. Keep steps 1–6, and replace the zip step:
  - derive the numeric core of `-Version`
  - `dotnet tool restore`
  - `dotnet wix extension add -g WixToolset.Util.wixext/<version read from .config/dotnet-tools.json>`
  - `dotnet wix build` into `artifacts/Pisum.Transcribe_<version>_win-x64.msi`
  - `dotnet wix msi validate -sice ICE61 -wx`
  - check the exit code of every call, clear an earlier MSI of the same name first, and print the version, the `ProductVersion`, the payload size and the MSI size
  - update the script's help text

  Verify: `./packaging/windows/build-msi.ps1 -Version 0.1.0-dev.1` creates `artifacts/Pisum.Transcribe_0.1.0-dev.1_win-x64.msi`, prints `ProductVersion 0.1.0`, the guard passing and no validation findings. `git status` shows nothing from `publish/` or `artifacts/`.
- [x] 2.2 Check that a failed validation stops the script: temporarily drop `-sice ICE61` and run it again. Verify: the script ends with a non-zero exit code, and there's no "Created" line. Then restore the flag, and check that `git diff` shows only the intended script changes.

## 3. Third-party notices (design D7)

- [x] 3.1 Add a **WiX Toolset** section to `THIRD-PARTY-NOTICES.md`:
  - the component: `Wix4UtilCA_X64`, embedded in the MSI, running only during an install, where it starts the app, and an uninstall, where it removes the startup entry
  - the source: `https://github.com/wixtoolset/wix`, tag `v6.0.2`
  - the license: MS-RL, with the text from that tag's `LICENSE.TXT`

  Verify: list the MSI's `Binary` table (Windows Installer COM, as in the spike). Every third-party entry in it has a section in the notices file, and `Wix4UtilCA_X64` is the only one.

## 4. Workflows (design D6)

- [x] 4.1 In `ci.yml`, replace "Build the zip" with "Build the MSI" (`./packaging/windows/build-msi.ps1 -Version $env:VERSION`), and upload `artifacts/*.msi` as `msi` with `if-no-files-found: error` and `retention-days: 7`. Verify: on the pull request, the run is green and has the `msi` artifact. Its log shows the VC++ runtime copied from a Visual Studio `Microsoft.VC14*.CRT` folder, the guard passing and `wix msi validate` passing.
- [x] 4.2 In `release.yml`:
  - `build` runs `build-msi.ps1` and uploads `artifacts/*.msi`.
  - `release` publishes exactly `artifacts/Pisum.Transcribe_${{ needs.version.outputs.version }}_win-x64.msi`.

  Verify: the workflow parses, and `git diff` shows the explicit `if:` conditions, `fail_on_unmatched_files` and `generate_release_notes` unchanged.

## 5. Documentation (design D8)

- [x] 5.1 Update `README.md`:
  - **Getting started:** download `Pisum.Transcribe_<version>_win-x64.msi`, open it, and at the SmartScreen prompt choose **More info** → **Run anyway**. Nothing else needs to be installed. The app starts when the install finishes, opens the model setup window on the first start, and is in the Start Menu. Give the MSI size and the installed size from 2.1.
  - **Upgrading:** install a newer MSI; if asked, let it close the running app. It starts the new version when it finishes.
  - **Uninstalling:** from Windows Settings → Apps. This keeps `%LOCALAPPDATA%\Pisum Transcribe\` with settings, logs and models, which the user deletes by hand to remove them.
  - **Zip users:** install the MSI, then delete the old folder.
  - **Project status:** installer and CI exist, automatic updates and signing don't.

  Verify: the README names no zip download except in the note for zip users.
- [x] 5.2 Update `packaging/README.md`:
  - `build-msi.ps1` and `Pisum.Transcribe.wxs`
  - the validation and why ICE61 is suppressed
  - the dual-purpose package, including D1's outcome from the spike (the UAC outcome comes from 6.2)
  - same-version upgrades, why the `afterInstallValidate` schedule must stay, and that going back from a release candidate to an older stable release means uninstalling first
  - the uninstall cleanup
  - the tool manifest and the extension pin
  - WiX's maintenance-fee terms
  - how to prove an MSI: install without administrator rights (1.3) and on a clean machine (Windows Sandbox or a VM, as before)

  Verify: every path it names exists, and `git grep -n build-zip` finds nothing outside `openspec/changes/archive/`.
- [x] 5.3 Update `CLAUDE.md`:
  - *Layout*: `packaging/windows/build-msi.ps1`, `Pisum.Transcribe.wxs` and `.config/dotnet-tools.json`
  - *Commands*: `./packaging/windows/build-msi.ps1 -Version 0.1.0-dev.1`

  Verify: `git grep -n build-zip CLAUDE.md` finds nothing.
- [x] 5.4 Update `docs/roadmap.md`:
  - Add `add-msi-installer` as step 13 after v1, with its GitHub issue.
  - Add `add-update-check` (GitHub #2) as the planned next step: a notice when a new version is released.
  - *Deferred*: replace the Velopack item with WinGet and Chocolatey packages (the Chocolatey id stays `pisum-transcribe`), code signing, and updating in one click, which waits for signing.

  Verify: `git grep -n -e add-msi-installer -e add-update-check docs/roadmap.md` finds both steps, and `git grep -n -i velopack docs/roadmap.md` finds nothing.

## 6. Release (design Migration Plan)

- [x] 6.1 Before the merge, rehearse the tag path on the branch: `git tag v0.1.0-rc.2` at the branch head, then push it. Verify:
  - the run skips `bump` and publishes **Pisum Transcribe v0.1.0-rc.2**, marked as a pre-release, with `Pisum.Transcribe_0.1.0-rc.2_win-x64.msi` and no zip
  - the downloaded MSI installs on this machine and starts the app
  - the app logs `0.1.0-rc.2+<sha>`, and Settings → Apps shows `0.1.0`

  Then delete the rehearsal with `gh release delete v0.1.0-rc.2 --cleanup-tag --yes`, so 6.3 can use the version. Verify: `gh release list` and `git ls-remote --tags origin` show no `v0.1.0-rc.2`.

  Rehearsed on 2026-09-22 from `4ac1cd3` (run 35697102544): all checks passed, and the app logged `0.1.0-rc.2+4ac1cd3…`. The MSI is kept for 6.2 as `artifacts\Pisum.Transcribe_0.1.0-rc.2_win-x64.msi`, SHA-256 `39C9D5C3B7F88EC652E8AE108114A129326680208372CA9846A91089AC00B14D`.
- [x] 6.2 Prove the MSI from 6.1 on a clean machine, meaning Windows Sandbox or a VM without .NET and without the Visual C++ Redistributable (spec: "Start on a clean machine", "Engine and silence trimming load on a clean machine"). Check the precondition first: `Test-Path C:\Windows\System32\vcruntime140.dll` is `False`. Verify: the MSI installs and starts the app without a runtime prompt, and after downloading Canary 180M Flash the tooltip shows **Ready (CPU)** or **Ready (Vulkan)**. The log shows the version and "Voice activity detection is ready".

  Also, on a machine with UAC on (this one, if its UAC is on), open the MSI as a user without an elevated session. Verify: no UAC prompt appears, and Task Manager shows the started app with "Elevated: No". These are the checks left from 1.3; the development machine has UAC off and can't prove them.

  Proved on 2026-09-22 in a VM with the MSI from 6.1: all checks passed, with no UAC prompt and "Elevated: No".
- [ ] 6.3 **After the merge:** start **Release** by hand with the exact version `0.1.0-rc.2` (`gh workflow run release.yml -f version=0.1.0-rc.2`). Verify: `main` gets "Bump the version to 0.1.0-rc.2", `v0.1.0-rc.2` exists, and the pre-release carries the MSI. The final `0.1.0` is a separate step.

## 7. Wrap-up

- [x] 7.1 Run `openspec validate add-msi-installer --strict` and `dotnet test Pisum.Transcribe.slnx`. Verify: both pass, and the last `ci.yml` run on the pull request is green with the `msi` artifact.
