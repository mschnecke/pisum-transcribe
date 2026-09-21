## 1. Continuous integration first (design D7)

- [x] 1.1 Add `.github/workflows/ci.yml`, based on `pisum-whisper`'s:
  - triggers: `pull_request` and `push` to `main`
  - `permissions: contents: read`
  - one job on `windows-latest`
  - steps: `actions/checkout@v4`, `actions/setup-dotnet@v4` with `global-json-file: global.json`, then `dotnet restore`, `dotnet build --no-restore` and `dotnet test`, all on `Pisum.Transcribe.slnx`, with no test filter.

  No zip step yet (it comes in 4.5). Push it on a branch and open a pull request. Verify: the run starts for the pull request, and the build step uses SDK 10.0.4xx, as its log shows.
- [x] 1.2 Read the first run's test results. For each failing class, find out why it fails on the runner (design D7):
  - If it needs something the runner doesn't have, make the test skip itself when that thing is missing, with `Assert.SkipWhen` and a reason naming it. For the tray icon, skip when `FindWindow("Shell_TrayWnd")` finds no window, which needs `FindWindow` in `NativeMethods.txt`. Never skip because of "running in CI".
  - Only if the missing thing can't be detected, move the class to Hardware (`Categories.Hardware`, `[Fact(Explicit = true)]`) following `CLAUDE.md`.
  - If it fails because of how the test is arranged, fix the test.
  - If more than a few classes fail, stop and split the fixes into their own change, as `pisum-whisper` did with `ready-the-suite-for-ci`.

  Verify: the pull request's run is green, and each skipped test is listed as skipped with its reason in the run output. Locally, `dotnet test Pisum.Transcribe.slnx` passes and still runs those tests.
- [x] 1.3 Merge the pull request. Verify: the Actions tab shows a `ci.yml` run for the resulting push to `main`.

## 2. One version (design D4)

- [x] 2.1 Add `<Version>0.0.0</Version>` to `Directory.Build.props`, with a comment adapted from `pisum-whisper`'s: the development default and the record of the last version, rewritten by `packaging/bump-version.sh`, and overridden by releases through `-p:Version`. Verify: `dotnet build Pisum.Transcribe.slnx` succeeds, and starting the app writes `Pisum Transcribe 0.0.0+<sha> starting` to the log.
- [x] 2.2 Copy `packaging/bump-version.sh` from `W:\github-pisum-whisper` without changes and add it with `git add --chmod=+x`. Verify: `git ls-files -s packaging/bump-version.sh` shows mode `100755`. In Git Bash, `./packaging/bump-version.sh 0.1.0-rc.1` prints `0.1.0-rc.1` and `./packaging/bump-version.sh patch` after it prints `0.1.0`, then `git checkout Directory.Build.props` restores `0.0.0`.

## 3. Third-party notices (design D5)

- [x] 3.1 Commit ONNX Runtime 1.30.0's `ThirdPartyNotices.txt` from the NuGet package root as `packaging/third-party/onnxruntime-ThirdPartyNotices.txt`, and add a sentence to the `Microsoft.ML.OnnxRuntime` pin comment in `Directory.Packages.props` saying to refresh that file when the pin changes. Verify: the committed file is identical to the one in `%NUGET_PACKAGES%\microsoft.ml.onnxruntime\1.30.0\` (`fc /b` or a hash).
- [x] 3.2 Complete `THIRD-PARTY-NOTICES.md` for every component that the zip ships (design D5): the NuGet packages, transcribe.cpp, ggml, miniz, libuiohook, the .NET runtime and the Microsoft Visual C++ runtime, plus the existing Silero VAD and ONNX Runtime sections. Take the license texts from the packages' own license files. For transcribe.cpp, ggml and miniz, take them from `transcribecppsharp.native.win-x64\0.2.3\runtimes\win-x64\native\licenses\`, not from the flattened `LICENSE` in the build output. Refer to `ThirdPartyNotices-OnnxRuntime.txt` for the components that ONNX Runtime bundles. Verify: every third-party `.dll` in a self-contained Release publish (`dotnet publish src/Pisum.Transcribe -c Release -r win-x64 --self-contained -o <scratch folder>`) belongs to a component with a section in the file.

## 4. The zip (design D2, D3, D9)

- [x] 4.1 Add the guard `packaging/windows/assert-native-dependencies.ps1 -Path <folder>` (design D9). It:
  - reads the import and delay-load import tables of every `.dll` and `.exe` in the folder with a small PE reader, not `dumpbin` or `objdump`
  - fails if an import named like a Visual C++ runtime file (`vcruntime*`, `msvcp*`, `concrt*`, `vcomp*`, `vccorlib*`) isn't in the folder, naming each missing file and the DLLs that import it
  - prints what it checked

  Verify two ways:
  - Run it on `src/Pisum.Transcribe/bin/Debug/net10.0-windows/win-x64`. It fails and names `VCRUNTIME140.dll`, `VCRUNTIME140_1.dll`, `MSVCP140.dll` and `MSVCP140_1.dll`, with `transcribe.dll` and `onnxruntime.dll` among the importers.
  - Run it on a scratch copy of that folder with those four files added from `System32`. It passes.
- [x] 4.2 Add `packaging/windows/build-zip.ps1 -Version <v>` and `packaging/windows/.gitignore` (which ignores `publish/`). The script:
  1. publishes self-contained ReadyToRun with `-p:PublishDocumentationFile=false`
  2. deletes `*.lib`
  3. writes the project `LICENSE` over the flattened one
  4. copies `THIRD-PARTY-NOTICES.md` and `ThirdPartyNotices-OnnxRuntime.txt`
  5. copies the Visual C++ runtime files the payload imports, including their own imports. The source is the newest `VC\Redist\MSVC\*\x64\Microsoft.VC14*.CRT\` that `vswhere` finds across all instances. It falls back to `System32` with a warning outside CI and fails in CI. It prints the source and the file versions (design D9).
  6. runs `assert-native-dependencies.ps1` on the folder
  7. zips the `Pisum Transcribe` folder into `artifacts\Pisum.Transcribe_<v>_win-x64.zip`

  It clears earlier output first and fails on the first error (`$ErrorActionPreference = 'Stop'`, and it checks the exit codes of `dotnet` and the guard). Verify: `./packaging/windows/build-zip.ps1 -Version 0.1.0-dev.1` creates the zip, prints the `System32` fallback warning on this machine, and reports that the guard passed. `git status` shows no files from `publish/` or `artifacts/`.
- [x] 4.3 Check the zip's contents and size:
  - There is exactly one top-level folder, `Pisum Transcribe`.
  - There are no `*.lib` files and no `Pisum.Transcribe.xml`, and `Pisum.Transcribe.pdb` is present.
  - `LICENSE` is the project's MIT license, and both notice files are present.
  - `vcruntime140.dll`, `vcruntime140_1.dll`, `msvcp140.dll` and `msvcp140_1.dll` are next to `Pisum.Transcribe.exe`.
  - `VoiceActivity\Assets\silero_vad.onnx` is present.

  Record the zip size and the extracted size for the README (6.1). Verify: an Explorer "Extract All" into an empty folder creates only `Pisum Transcribe`.
- [ ] 4.4 Prepare a clean machine and prove the zip on it (spec: "Start on a clean machine", "Engine and silence trimming load on a clean machine"):
  - Use Windows Sandbox (enable it once, as administrator, with `Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All`, then restart) or a VM with neither .NET nor the VC++ redistributable.
  - Check the precondition first: in the sandbox, `Test-Path C:\Windows\System32\vcruntime140.dll` is `False` and `dotnet` isn't found. If `vcruntime140.dll` exists there, the machine isn't clean enough to prove D9. Use a VM instead.
  - Extract the zip and start `Pisum.Transcribe.exe`, then download Canary 180M Flash (208 MB) in the setup window.

  Verify:
  - The tray icon appears without a runtime prompt, and the tooltip then shows **Ready (CPU)** or **Ready (Vulkan)**.
  - The sandbox's `%LOCALAPPDATA%\Pisum Transcribe\logs\` shows `Pisum Transcribe 0.1.0-dev.1+<sha> starting` and "Voice activity detection is ready".
  - Note the SmartScreen dialog text for the README, if it appears.
- [x] 4.5 Add a step to `ci.yml` after the tests that runs `build-zip.ps1` with `VERSION: 0.1.0-ci.${{ github.run_number }}` and uploads `artifacts/*.zip` (`if-no-files-found: error`, `retention-days: 7`), as `pisum-whisper` does for its MSI. Verify: a pull request run is green and has the zip as a downloadable artifact. Its log shows the VC++ runtime copied from a Visual Studio `Microsoft.VC14*.CRT` folder, not `System32`, and the guard passing.

## 5. Release workflow (design D1, D4)

- [ ] 5.1 Add `.github/workflows/release.yml`, based on `pisum-whisper`'s:
  - jobs `bump`, `version`, `build` and `release`, with the same triggers, inputs, `permissions: contents: write` and comments
  - no `chocolatey` or `homebrew` job and no matrix
  - `build` runs on `windows-latest`. It checks out the tag, runs `dotnet test Pisum.Transcribe.slnx -c Release` and then `build-zip.ps1`, and uploads the zip.
  - `release` publishes exactly `artifacts/Pisum.Transcribe_<version>_win-x64.zip` with name `Pisum Transcribe v<version>`, the `prerelease` output, `fail_on_unmatched_files: true` and `generate_release_notes: true`.

  Verify: the workflow parses (the Actions tab lists **Release** with its dispatch inputs after the push).
- [ ] 5.2 Before the merge, rehearse the tag path on the branch. A pushed tag runs the workflow file from the commit it points at, so this works before `release.yml` is on `main`: `git tag v0.1.0-rc.1` at the branch head, then `git push origin v0.1.0-rc.1`. Verify:
  - The run skips `bump`, and `version` derives `0.1.0-rc.1` with `prerelease` true.
  - The Releases page shows `Pisum Transcribe v0.1.0-rc.1`, marked as a pre-release, with `Pisum.Transcribe_0.1.0-rc.1_win-x64.zip` attached.
  - On the clean machine from 4.4, that zip starts, reaches **Ready** with a downloaded model, and logs `0.1.0-rc.1`.

  Then delete the rehearsal (`gh release delete v0.1.0-rc.1 --cleanup-tag --yes`), so 5.3 can use the same version. Verify: `gh release list` and `git ls-remote --tags origin` show no `v0.1.0-rc.1`.
- [ ] 5.3 **After the merge**, tracked in the change's GitHub issue: GitHub offers `workflow_dispatch` only for workflows on the default branch. Start **Release** by hand with the exact version `0.1.0-rc.1` (`gh workflow run release.yml -f version=0.1.0-rc.1`). Verify:
  - The run commits `Bump the version to 0.1.0-rc.1` to `main` and pushes `v0.1.0-rc.1`.
  - The pre-release `Pisum Transcribe v0.1.0-rc.1` is published with its zip.
- [ ] 5.4 **After the merge**, tracked in the issue: check the refusal. Start **Release** again with the exact version `0.1.0-rc.1`. Verify: the `bump` job fails with "v0.1.0-rc.1 already exists", and `main` has no new commit.
- [ ] 5.5 **After the merge:** optionally delete the `v0.1.0-rc.1` release and tag again. Leave the bump commit, so the next `patch` gives `0.1.0` (design D4). Don't publish `v0.1.0` in this change. Verify: `Directory.Build.props` on `main` says `0.1.0-rc.1`. Then close the change's issue by hand once its checklist is done. The PR references the issue with "Part of #N", never with a closing keyword.

## 6. Documentation (design D8)

- [x] 6.1 Update `README.md`:
  - *Getting started* first: download `Pisum.Transcribe_<version>_win-x64.zip` from GitHub Releases, extract it to a permanent folder (because **Start with Windows** stores the exe path; after moving the folder, start the app once from its new place, which repairs the entry), start `Pisum.Transcribe.exe`, and choose **More info** → **Run anyway** at the SmartScreen prompt. Include the sizes from 4.3. Nothing else needs to be installed: .NET and the Visual C++ runtime are in the zip, and a Vulkan GPU driver is optional.
  - *Build from source* second, with the GitHub clone URL.
  - Issues on GitHub, and *Project status* updated: CI and zip releases exist, an installer, auto-update and signing don't yet.

  Verify: `git grep -n -i gitlab README.md` finds nothing.
- [x] 6.2 Update `CLAUDE.md`:
  - *Repository*: GitHub `mschnecke/pisum-transcript`, remote `git@github.pisum:…`, `gh` for issues and PRs, PRs to `main`.
  - *Layout*: `.github/workflows/` and `packaging/`.
  - *Commands*: `build-zip.ps1`, `bump-version.sh`, and starting **Release** with `gh workflow run release.yml -f bump=patch`.

  Verify: `git grep -n -i gitlab CLAUDE.md` finds nothing.
- [x] 6.3 Update `docs/roadmap.md`:
  - Line 3: a GitHub issue per change.
  - The v1 table stays, without links, with one sentence saying v1 was tracked internally. The same sentence says that the issue and MR numbers in the archived changes (`#1` to `#14`, `!7`, `!11`) refer to that GitLab tracker, not to GitHub.
  - Add `add-packaging-ci` as a step.
  - *Deferred*: remove the CI item and keep Velopack, code signing and WinGet/Chocolatey. The Chocolatey item says the package id is `pisum-transcribe`, because `pisum-transcript` on the MyGet feed belongs to the old app and has versions up to 1.0.5 (design Non-Goals).

  Verify: `git grep -n gitlab docs/roadmap.md` finds nothing, and `git grep -n pisum-transcribe docs/roadmap.md` finds the Chocolatey item.
- [x] 6.4 Add `packaging/README.md`, based on `pisum-whisper`'s. It covers:
  - the scripts and their one argument
  - both ways to start a release
  - the bump rules and the `0.0.0` seed
  - the unsigned decision
  - the local Visual C++ runtime, where it comes from and what the guard checks, and how to prove a zip on a clean machine (Windows Sandbox and its precondition)
  - the ONNX Runtime notices refresh
  - that protecting `main` needs a bypass for GitHub Actions

  Verify: every path it names exists.

## 7. Wrap-up

- [ ] 7.1 Run `openspec validate add-packaging-ci --strict` and `dotnet test Pisum.Transcribe.slnx`. Verify: both pass, and the last `ci.yml` run on the pull request is green, with the zip artifact.
