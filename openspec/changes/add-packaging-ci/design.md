## Context

See proposal.md, Why. The shape comes from `pisum-whisper` (`W:\github-pisum-whisper`), whose CI and release have published v1.0.0 to v1.0.2. Its `add-packaging-ci` change (archived 2026-09-03) is the reference. Its later bump workflow is the `release.yml` copied here.

Current state that the approach depends on:
- **No version.** `Program.cs` logs `AssemblyInformationalVersion` at start, and nothing sets a version, so every build reports `1.0.0+<sha>`.
- **The default test run is hermetic.** It has 43 Unit test classes and 1 Integration test class. The tests in all 8 Hardware test classes are `[Fact(Explicit = true)]`. Four classes in the default run still use real Windows services:

  | Test class | Category | Uses |
  |---|---|---|
  | `Tray/TrayIconServiceTests` | Unit | a real tray icon (`Shell_NotifyIcon`) on an STA thread |
  | `SettingsWindow/SettingsDialogTests` | Unit | a real WPF window |
  | `Hosting/DispatcherWaitTests` | Unit | a real WPF dispatcher on an STA thread |
  | `SettingsWindow/UserRegistryTests` | Integration | `HKCU`, in a test subkey it deletes afterwards |

- **The build output** (framework-dependent, Debug) is 85 MB in 70 files. `ggml-vulkan.dll` is 52 MB and `onnxruntime.dll` is 16 MB. The build output also contains:
  - `onnxruntime.lib` and `onnxruntime_providers_shared.lib`, which are import libraries needed only at link time
  - `Pisum.Transcribe.xml`, the XML doc file that `GenerateDocumentationFile` produces
  - a `LICENSE` file. `TranscribeCppSharp.Native.win-x64` 0.2.3 ships three license files under `runtimes/win-x64/native/licenses/`: transcribe.cpp's, `ggml/LICENSE` and `src/third_party/miniz/LICENSE`. NuGet flattens native assets, so all three are copied to `LICENSE` in the output folder and the last one wins. The file in the output is miniz's, and the transcribe.cpp and ggml texts are lost.
- **The native DLLs need the Visual C++ runtime.** Their import tables, read from the build output:

  | DLL | Imports besides Windows' own UCRT (`api-ms-win-crt-*`) |
  |---|---|
  | `transcribe.dll` and all 12 `ggml*.dll` | `VCRUNTIME140.dll`, `VCRUNTIME140_1.dll`, `MSVCP140.dll` |
  | `onnxruntime.dll` | the same, plus `MSVCP140_1.dll` |
  | `onnxruntime_providers_shared.dll` | `VCRUNTIME140.dll` |
  | `ggml-vulkan.dll` | also `vulkan-1.dll`, from the GPU driver. When it's missing, the Vulkan backend doesn't load and the app runs on the CPU, as it already does. |
  | `uiohook.dll` | nothing |

  These files come from the Visual C++ 2015 to 2022 Redistributable, which a clean Windows doesn't have. A self-contained .NET app doesn't bring them: .NET links the C runtime statically, and WPF ships only a renamed private copy, `vcruntime140_cor3.dll`, that doesn't satisfy these imports. The development machine and GitHub's Windows runners both have the redistributable in `System32`, so neither local testing nor CI notices the dependency. On this machine, `vswhere` finds only SQL Server Management Studio 22, which has no `Microsoft.VC14*.CRT` folder, and Windows Sandbox is not enabled.
- **`Microsoft.ML.OnnxRuntime` 1.30.0** has an exact pin, and its package ships `ThirdPartyNotices.txt` (6,369 lines) for the components it bundles. `THIRD-PARTY-NOTICES.md` lists only Silero VAD and ONNX Runtime today.
- **The GitHub repository** is public. `main` is unprotected and has received direct pushes, and the repository has no secrets and no releases.
- **The repository name had an earlier owner.** "Pisum Transcript", a Tauri app for macOS and Windows, lived at `mschnecke/pisum-transcript` until it was renamed to `mschnecke/pisum-transcript-one`, which is now private, at v1.0.5. It has no in-app updater: no updater plugin in `tauri.conf.json` or `Cargo.toml`, and no update check in its source. Two package channels still point at release files under this repository's name, and both fail with a 404 today, because the files moved with the renamed repository and are no longer public:

  | Channel | Package | Points at |
  |---|---|---|
  | Homebrew tap `mschnecke/homebrew-pisum-transcript` | cask `pisum-transcript` 1.0.1 | `…/pisum-transcript/releases/download/v1.0.1/Pisum.Transcript_1.0.1_aarch64.pkg` |
  | MyGet feed `mschnecke` (Chocolatey) | `pisum-transcript` 0.1.19 to 1.0.5 | `…/pisum-transcript/releases/download/v<version>/Pisum.Transcript_<version>_x64_en-US.msi`, with a pinned checksum |

  The id `pisum-transcribe` is not used on the MyGet feed.
- **Line endings:** `core.autocrlf` is `input` on this machine, so a committed shell script is stored with LF. Git for Windows doesn't record the executable bit on its own.

## Goals / Non-Goals

**Goals:**
- Match `pisum-whisper` wherever the two projects don't differ, so one person operates both releases the same way.
- The packaging is a script that a person can run locally and get the same zip the workflow publishes. Nothing about a release exists only in `.github/workflows/`.
- Every change builds the zip in CI, so the packaging doesn't rot between releases.

**Non-Goals:**
- Changing application code for packaging, such as an update check, a stable install path or uninstall cleanup. That is the Velopack change.
- Retiring the old app's package channels, the Homebrew tap and the MyGet package (see Risks).
- WinGet and Chocolatey packages. When Chocolatey comes, its package id SHALL be `pisum-transcribe`, not `pisum-transcript`. The old id already has versions up to 1.0.5, which would outrank this app's 0.x versions and put two products under one id. The roadmap records this (tasks 6.3).
- Release notes beyond what GitHub generates.

## Decisions

### D1: Copy `pisum-whisper`'s two workflows, with four differences

`ci.yml` and `release.yml` keep `pisum-whisper`'s jobs, triggers and actions (`actions/checkout@v4`, `actions/setup-dotnet@v4` with `global-json-file`, `actions/upload-artifact@v4`, `actions/download-artifact@v4`, `softprops/action-gh-release@v2`):

```
release.yml   on: push tags v*  |  workflow_dispatch(bump: patch|minor|major, version: exact)

  [bump]     ubuntu    dispatch only: bump-version.sh, commit, tag, push branch then tag
     |
  [version]  ubuntu    always(): X.Y.Z, vX.Y.Z, prerelease = version contains "-"
     |
  [build]    windows   checkout the tag, test, build-zip.ps1 -Version X.Y.Z, upload-artifact
     |
  [release]  ubuntu    download-artifact, action-gh-release (tag, name, prerelease,
                       fail_on_unmatched_files, the one zip by exact name)
```

The dispatch run doesn't wait for its own tag to start a second run. A push made with `GITHUB_TOKEN` raises no workflow event, so the same run continues with the tag it pushed, as in `pisum-whisper`.

| | `pisum-whisper` | here | Why |
|---|---|---|---|
| Artifact | MSI + pkg | zip | An installer comes with Velopack. A zip needs no WiX and no install step to verify. |
| Tests in the release run | no | `dotnet test -c Release` in `[build]`, before the zip | `main` gets direct pushes, so a tag can point at a commit that CI never ran. |
| CI triggers | `pull_request` | `pull_request` and `push` to `main` | the same reason |
| Platforms | matrix of two | `windows-latest` only | No matrix and no `fail-fast` setting. |

The `chocolatey` and `homebrew` jobs are not copied. One addition: `[release]` sets `generate_release_notes: true`, so each release lists its commits and PRs and links to the full compare view. `ci.yml` also builds the zip and uploads it as a workflow artifact with 7-day retention, as `pisum-whisper` does for its MSI.

*Rejected:* a single tag-only workflow without the bump job. That is simpler, but every release would mean editing `<Version>` and pushing a tag by hand, and the two projects would work differently.

### D2: Self-contained and ReadyToRun; not single-file, not trimmed

`dotnet publish src/Pisum.Transcribe -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true -p:Version=<v>`. The project already sets `RuntimeIdentifier`, but since .NET 8 that doesn't imply self-contained, so the flag is explicit.

- **Self-contained**, together with the local VC++ runtime (D9), is what makes "extract and start" true on a clean machine. The price is the .NET and WPF runtime in the zip.
- **ReadyToRun**, as in `pisum-whisper`. With **Start with Windows**, the app starts at sign-in, when the machine is busiest. The framework assemblies are precompiled already. ReadyToRun precompiles the app and NuGet assemblies too, so they need no JIT at start. It makes those assemblies larger, which is small next to the native libraries.
- **Not single-file:** `IncludeNativeLibrariesForSelfExtract` would extract `transcribe.dll`, the `ggml*.dll` files, `onnxruntime.dll` and `uiohook.dll` to a temporary folder on first start. That adds a path the app doesn't control to the native loading, which `TranscribeCppSharp`'s resolver and ONNX Runtime's version check (the reason for the exact pin) both depend on. A zip holds a folder as easily as one file.
- **Not trimmed:** the SDK doesn't support trimming WPF apps, and `TreatWarningsAsErrors` would turn any IL2xxx warning into an error anyway.

### D3: The payload is assembled by `packaging/windows/build-zip.ps1`

`build-zip.ps1 -Version <v>` is the one command that both a person and both workflows run:

1. It publishes (D2) into `packaging/windows/publish/Pisum Transcribe/`, with `-p:PublishDocumentationFile=false`. `packaging/windows/.gitignore` keeps that folder untracked.
2. It deletes `*.lib`, which are import libraries that nothing loads at run time.
3. It writes the project's `LICENSE` over the flattened `LICENSE` from the native package (see Context). The transcribe.cpp, ggml and miniz license texts live in `THIRD-PARTY-NOTICES.md` instead (D5), so nothing is lost by overwriting.
4. It copies `THIRD-PARTY-NOTICES.md`, and `packaging/third-party/onnxruntime-ThirdPartyNotices.txt` as `ThirdPartyNotices-OnnxRuntime.txt`.
5. It copies the Visual C++ runtime DLLs that the payload imports (D9).
6. It runs the guard `packaging/windows/assert-native-dependencies.ps1` on the folder (D9) and stops if the guard fails.
7. It zips the `Pisum Transcribe` folder itself, not its contents, into `artifacts/Pisum.Transcribe_<v>_win-x64.zip`. `artifacts/` is already in `.gitignore`.

The managed `.pdb` files stay in the zip. `-p:DebugType=none` is rejected: the app logs unhandled exceptions (app-shell), and a stack trace without line numbers from a released build is the report nobody can act on. Only `Pisum.Transcribe.pdb` is ours, and it is small. NuGet packages rarely ship `.pdb` files into the publish output, but if a large native one turns up, the script deletes it, as `pisum-whisper`'s D2 did.

Removals happen in the script after publishing, not through MSBuild properties in the project, so `src/` doesn't change and the payload is defined in one file.

### D4: One version, recorded in `Directory.Build.props` and taken from the tag

`Directory.Build.props` gets `<Version>0.0.0</Version>`, and `packaging/bump-version.sh` is copied from `pisum-whisper` byte for byte. It's added with `git add --chmod=+x`, because `[bump]` runs it directly on Ubuntu. Releases pass `-p:Version` from the tag, which overrides the file, and the SDK appends `+<sha>` to the informational version the app logs (`0.1.0+1a2b3c4`).

The seed is `0.0.0`, not `0.1.0`: `bump-version.sh` refuses to "bump" to the version the file already has, and a patch bump from `0.1.0` would skip `0.1.0`. The first release goes like this:

```
0.0.0  --dispatch, exact 0.1.0-rc.1-->  0.1.0-rc.1  (pre-release, rehearsal)
       --dispatch, patch-------------->  0.1.0       (suffix dropped, not 0.1.1)
```

`Directory.Build.props` applies to the test project too, which does no harm.

### D5: `THIRD-PARTY-NOTICES.md` covers what the zip ships

The file keeps its current shape: one section per component, with name, version, source, license and the full license text where the license requires it. It is completed from the publish output, not from `Directory.Packages.props`, because the zip also ships transitive and native components:

- **NuGet packages:** CommunityToolkit.Mvvm, H.NotifyIcon and H.NotifyIcon.Wpf (with H.GeneratedIcons.System.Drawing), NAudio (Core and Wasapi), Serilog (core, Extensions.Hosting, Extensions.Logging, Sinks.File), SharpHook, TranscribeCppSharp (with Interop), Microsoft.ML.OnnxRuntime
- **Native libraries:** transcribe.cpp, ggml (with its CPU variants and Vulkan), miniz, libuiohook
- **.NET:** the .NET runtime, WPF and Microsoft.Extensions.*, under MIT from the .NET Foundation
- **Microsoft Visual C++ runtime** (D9): redistributed under the Visual Studio license terms for distributable code. It isn't in the publish output, because the script adds it.
- **Existing sections:** Silero VAD

ONNX Runtime's own `ThirdPartyNotices.txt` is committed as `packaging/third-party/onnxruntime-ThirdPartyNotices.txt`. That's simpler than finding the NuGet cache from a script. It's tied to the exact 1.30.0 pin, so the pin's comment in `Directory.Packages.props` gets a line that says to refresh it.

The speech models are not in the zip, so their licenses stay in the README (CC BY 4.0), where they are today.

### D6: Unsigned, by decision

The zip and the exe are not code-signed. On first start, SmartScreen shows "Windows protected your PC". The README tells users to choose **More info**, then **Run anyway**.

*Rejected for now:*
- **SignPath Foundation**: free for open source, but it needs an application and a review, and it signs through their pipeline.
- **Azure Artifact Signing**: paid, and it needs identity validation.

Both fit better with Velopack, which signs its `Setup.exe` and the app in one step.

### D7: `ci.yml` lands first, and its first run decides the test question

`pisum-whisper`'s first CI run failed on tests, not packaging, and needed its own change (`ready-the-suite-for-ci`). GitHub's hosted Windows runners run with a signed-in desktop session, so the four classes in Context will probably pass. The tray icon is the least certain, because it needs the shell's notification area. The first `ci.yml` run on a pull request is the test.

If a class fails only on the runner because the runner lacks something it needs, the test skips itself when that thing is missing, with `Assert.SkipWhen` and a reason. For the tray icon, that means skipping when there is no notification area, for example when `FindWindow("Shell_TrayWnd")` finds no window. The test keeps running in everyone's default local run, and CI reports it as skipped with its reason. The condition is the missing capability, never "running in CI", so a runner that gains the capability runs the test again. Only if the condition can't be detected is the class moved to Hardware (`[Fact(Explicit = true)]`, `Categories.Hardware`), the rule in `CLAUDE.md` for tests that need the real desktop. That is a last resort, because it takes the test out of the default run on every machine.

CI doesn't get a filter: `dotnet test Pisum.Transcribe.slnx` stays the one command locally and in CI, so what CI skips is visible in the source and in the run output, not hidden in a workflow file. A test that fails because of how it is arranged (timing, ordering) is fixed, not skipped. If there are more than a few such failures, they go into a separate change first, as in `pisum-whisper`.

### D8: GitHub is the project's home in the docs

| File | Change |
|---|---|
| `README.md` | Clone URL from GitHub. *Getting started* starts with downloading the zip from Releases and extracting it to a permanent folder, with the SmartScreen note (D6) and a note that **Start with Windows** stores the exe path. Building from source comes second. Work tracked in GitHub issues. *Project status*: CI and zip releases exist, installer, auto-update and signing don't. |
| `CLAUDE.md` | *Repository*: `mschnecke/pisum-transcript`, remote `git@github.pisum:…`, `gh` for issues and PRs, PRs to `main`. *Layout*: `.github/workflows/` and `packaging/`. *Commands*: `build-zip.ps1` and `bump-version.sh`, and how to start a release. |
| `docs/roadmap.md` | Line 3: a GitHub issue per change. The v1 table stays as history without the GitLab links, which outsiders can't open, with one sentence saying v1 was tracked internally. *Deferred*: CI and zip release move out. Velopack, signing and WinGet/Chocolatey stay. |
| `packaging/README.md` | New, based on `pisum-whisper`'s: the scripts, how to release, D4's seed and bump rules, and the requirement that `main` accept pushes from `GITHUB_TOKEN` (see Risks). |

`docs/idea.md` and the archived changes are history and stay as they are.

### D9: The Visual C++ runtime ships next to the exe, and a guard fails the build when a runtime import is missing

**Local deployment.** `build-zip.ps1` copies the Visual C++ runtime DLLs into the `Pisum Transcribe` folder, next to `Pisum.Transcribe.exe`. Microsoft documents this as "local deployment" of the redistributable files. When Windows loads `transcribe.dll`, `ggml*.dll` or `onnxruntime.dll`, it looks for their imports in the application folder first, so the local copies are found on a machine without the redistributable.

- **Which files:** exactly the ones the payload imports, found by reading the import tables (the same scan as the guard), including the imports of the copied files themselves (`msvcp140.dll` imports `vcruntime140.dll`). Today that is `vcruntime140.dll`, `vcruntime140_1.dll`, `msvcp140.dll` and `msvcp140_1.dll`, about 1 MB. A future package that needs another runtime file, such as `vcomp140.dll` for OpenMP, gets it without a script change.
- **Where from:** the newest `VC\Redist\MSVC\<version>\x64\Microsoft.VC14*.CRT\` folder in any Visual Studio instance `vswhere` finds, searching all instances, not just `-latest`, which finds SQL Server Management Studio on this machine. GitHub's `windows-latest` image has Visual Studio with the C++ tools. A run outside CI that finds no such folder falls back to `%WINDIR%\System32` and prints a warning, so a person can build a zip locally. In CI (`$env:CI` is `true`), a missing folder is an error: a published release takes the files from the redist folder, which the Visual Studio license terms cover for redistribution.
- The script prints the source folder and the file versions it copied, so a release's run log shows which runtime it shipped.

**The guard.** `packaging/windows/assert-native-dependencies.ps1 -Path <folder>` is its own script, so it can run on any folder, the build output included:
1. It reads the import table and the delay-load import table of every `.dll` and `.exe` in the folder.
2. Every import whose name is a Visual C++ runtime file (`vcruntime*`, `msvcp*`, `concrt*`, `vcomp*`, `vccorlib*`) must exist in the folder.
3. If one is missing, it fails and names the missing file and every DLL that imports it.

It checks only the VC runtime family. It doesn't allowlist Windows DLLs, which would be brittle across Windows versions. UCRT (`api-ms-win-crt-*`, `ucrtbase.dll`) is part of Windows 10 and later, and `vulkan-1.dll` is optional by design. The import tables are read with a small PE reader in PowerShell: `dumpbin` exists only where Visual Studio does, and `objdump` isn't on the runner or in Git for Windows.

The guard runs in every `build-zip.ps1` run, so in every CI run and every release. It replaces a clean machine as the automatic check. The clean-machine check in Windows Sandbox or a VM stays as a manual proof (tasks 4.4 and 5.2), because the guard knows only about the VC runtime family.

*Rejected:*
- **README prerequisite** ("install the VC++ Redistributable first"): it breaks "extract and start". Without it, the app reaches the tray and then fails to load its engine, which looks like a broken app, not a missing prerequisite.
- **Bundling `vc_redist.x64.exe`:** a zip can't run an installer. This is the right approach for Velopack, which can install the redistributable as a prerequisite of its `Setup.exe`, and the Velopack change should switch to that.
- **Asking the upstream packages to link the C runtime statically:** not in our control, and ONNX Runtime's official Windows builds link it dynamically.

## Risks / Trade-offs

- [A test in the default run fails on the runner] → D7: CI lands first. The fix is a skip on the missing capability, a fix to the test, or, as a last resort, a move to Hardware, never a filter.
- [SmartScreen warns on every download until the exe builds reputation] → documented in the README. Signing comes with Velopack (D6).
- [A user enables **Start with Windows** and then moves the extracted folder, so the `Run` entry points to a missing exe and nothing starts at sign-in] → The README tells users to extract to a permanent folder first. Velopack's stable install path removes this later. Changing `StartupRegistration` is out of scope.
- [The bump job pushes to `main` with `GITHUB_TOKEN`, so branch protection on `main` would reject it after the version is decided but before anything is tagged or published] → `main` is unprotected today. `packaging/README.md` says that protecting `main` needs a bypass for GitHub Actions or a PAT. A refused push fails the run before a tag exists, so nothing half-published is left behind.
- [`bump-version.sh` loses its executable bit or gets CRLF from a Windows checkout] → `git add --chmod=+x`, and `core.autocrlf=input` stores LF. `[bump]` fails loudly on either, before anything is committed.
- [The local VC++ runtime copies don't get Windows Update fixes, as a centrally installed redistributable does] → The copies come from the newest Visual Studio on the runner at release time, so each release refreshes them. The Velopack change switches to installing the redistributable as a prerequisite (D9).
- [A DLL with the same name that is already loaded wins over the local copy: if another component loads an older `msvcp140.dll` from `System32` into the process first, ONNX Runtime uses that one] → This risk exists today with or without local copies, and local deployment doesn't make it worse. It's visible as an ONNX Runtime load failure in the log, and D9's rejected alternatives don't avoid it either.
- [The guard knows only the VC runtime family, so a future native dependency outside it would slip through] → The manual clean-machine check (tasks 4.4 and 5.2) catches it, and the guard's pattern list is one line to extend.
- [The zip is large, about 150 MB extracted] → an estimate from the 85 MB framework-dependent output plus the .NET and WPF runtime, and most of it is `ggml-vulkan.dll`. It's measured in the tasks and written in the README. It doesn't change the approach: the CPU-only native package would halve the size and remove the GPU, which is the app's main advantage.
- [ONNX Runtime's notices copy goes stale when the pin changes] → the pin's comment says to refresh it (D5).
- [The old app's Homebrew cask and MyGet Chocolatey package still download from this repository's release URLs (see Context)] → Neither can install this app by accident: both ask for `Pisum.Transcript_*` files, which this change never publishes, the Chocolatey packages pin a checksum, and this change never dispatches to the tap. They keep failing with a 404, as they do today. The old app has no updater, so installed copies never contact this repository. What remains is two public entry points that lead to the wrong project. Retiring them is a separate act by the maintainer, best done before the first release without a suffix:
  - Homebrew: remove `casks/pisum-transcript.rb`, so `brew` reports no such cask instead of failing on a download, and archive the tap repository.
  - MyGet: delete or unlist the `pisum-transcript` versions on the `mschnecke` feed.
- [`softprops/action-gh-release` is a third-party action with `contents: write`] → The same major-version pin as `pisum-whisper`. Pinning to a SHA is a repository-wide decision for both projects, not this change's.

## Migration Plan

Nothing is installed, so there is nothing to migrate. The rollout order is the risk order:

1. `ci.yml` on a branch and a pull request. Its run is the D7 test. Fix tests, or make them skip on a missing capability, until it's green.
2. `packaging/`, `<Version>0.0.0</Version>`, the completed notices, and the zip step in `ci.yml`. Run the guard on the build output first, where it must fail (D9). Then run `build-zip.ps1` locally, and start the result on a clean machine: Windows Sandbox, once enabled, or a VM with neither .NET nor the VC++ redistributable. Check that it reaches the tray, loads a model and logs its version.
3. `release.yml`. Rehearse by dispatching with the exact version `0.1.0-rc.1`, check the pre-release and its zip on the clean machine, then delete that release and tag if you like. The bump commit stays, and the next `patch` gives `0.1.0` (D4).
4. The docs (D8).
5. Publishing `v0.1.0` is a separate act after the change is merged, ideally after the old app's package channels are retired (see Risks).

**Rollback:** delete the workflow files. `src/` doesn't change behaviour. The only edits outside `packaging/`, `.github/` and the docs are `<Version>` in `Directory.Build.props` and a comment in `Directory.Packages.props`. A bad release is deleted from the Releases page, together with its tag.
