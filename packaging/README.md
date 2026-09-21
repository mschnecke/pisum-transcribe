# packaging

Everything that turns a build into a release. Nothing here is compiled into the app or read at run time, and `dotnet build` and `dotnet test` never look in this folder.

| Path | What it is |
|---|---|
| `bump-version.sh` | Decides the version of the next release and writes it into `Directory.Build.props` |
| `windows/build-zip.ps1` | Publishes the app and zips it into `artifacts/Pisum.Transcribe_<version>_win-x64.zip` |
| `windows/assert-native-dependencies.ps1` | The guard: fails when a library in a folder imports a Visual C++ runtime file that the folder doesn't contain |
| `windows/.gitignore` | Keeps `windows/publish/`, where `build-zip.ps1` assembles the app folder, untracked |
| `third-party/onnxruntime-ThirdPartyNotices.txt` | ONNX Runtime's notices for the components it bundles, shipped in the zip as `ThirdPartyNotices-OnnxRuntime.txt` |

## The scripts

Each script takes one argument. A person runs the same command as the workflows, so nothing about a release exists only in `.github/workflows/`.

```sh
./packaging/bump-version.sh patch                                    # Git Bash: 0.1.0 -> 0.1.1, prints 0.1.1
./packaging/windows/build-zip.ps1 -Version 0.1.0-dev.1               # PowerShell 7: the zip, in artifacts\
./packaging/windows/assert-native-dependencies.ps1 -Path <folder>    # PowerShell 7: the guard on any folder
```

`build-zip.ps1` does this, and stops at the first error:

1. It publishes `src/Pisum.Transcribe` self-contained and ReadyToRun into `windows/publish/Pisum Transcribe/`, with the version passed as `-p:Version`. It isn't single-file and isn't trimmed (design D2 of `add-packaging-ci`).
2. It deletes the `*.lib` import libraries, which nothing loads at run time.
3. It writes the project's `LICENSE` over the one from the transcribe.cpp native package. That package ships three license files, which NuGet flattens into one `LICENSE`. `dotnet publish` refuses that (NETSDK1152), so the script passes `-p:ErrorOnDuplicatePublishOutputFiles=false`. The three texts are in `THIRD-PARTY-NOTICES.md`.
4. It copies `THIRD-PARTY-NOTICES.md` and ONNX Runtime's notices into the folder.
5. It copies the Visual C++ runtime files that the payload imports (see below).
6. It runs the guard on the folder.
7. It zips the `Pisum Transcribe` folder itself, so extracting the zip creates that one folder.

The zip is about 94 MB, and the folder is about 230 MB, most of it `ggml-vulkan.dll`, `onnxruntime.dll` and the .NET and WPF runtime. `Pisum.Transcribe.pdb` stays in the zip, so a logged stack trace has line numbers.

## Versions

`Directory.Build.props` holds the version of the last release, in `<Version>`. A build that isn't a release reports that version. A release takes its version from the tag, without the `v`, and passes it to `build-zip.ps1`, so the zip name, the release name and the version the app logs at start agree.

`bump-version.sh` reads `<Version>`, writes the next one and prints it. It doesn't touch git: the edit is one line that `git diff` shows and `git checkout` undoes.

```sh
./packaging/bump-version.sh patch        # 0.1.0 -> 0.1.1
./packaging/bump-version.sh minor        # 0.1.0 -> 0.2.0
./packaging/bump-version.sh 0.2.0-rc.1   # an exact version, for a pre-release
```

- **A keyword bump from a pre-release drops the suffix.** `0.2.0-rc.1` was a rehearsal for `0.2.0`, so `patch` gives `0.2.0`, not `0.2.1`. For a further pre-release, pass the exact version.
- **It refuses to write the version the file already has.** That is why the seed is `0.0.0`, not `0.1.0`: the first release is the exact version `0.1.0-rc.1`, and the `patch` after it gives `0.1.0`.

## Releasing

There are two ways to start a release, and both end the same way. The **Release** workflow (`.github/workflows/release.yml`) runs the tests from the tagged commit, builds the zip, and publishes it on GitHub Releases with generated release notes. If a test or the build fails, nothing is published. A version with a suffix, such as `0.2.0-rc.1`, is published as a pre-release.

- **By hand:** start **Release** in the Actions tab, or with `gh`:

  ```sh
  gh workflow run release.yml -f bump=patch          # or minor, major
  gh workflow run release.yml -f version=0.2.0-rc.1  # an exact version
  ```

  The run bumps the version, commits `Bump the version to <version>` to the branch it was started from, pushes the tag, and publishes the release in the same run. A push made with `GITHUB_TOKEN` starts no workflow, so the run doesn't wait for its own tag. If the tag already exists, the run fails before it commits anything.
- **With a tag:** bump the version and commit it, then push a tag for it:

  ```sh
  ./packaging/bump-version.sh 0.2.0
  git commit -am "Bump the version to 0.2.0"
  git tag v0.2.0
  git push origin main v0.2.0
  ```

  The tag push skips the bump job. A pushed tag runs the workflow file from the commit it points at, so a tag on a branch can rehearse a change to `release.yml` before it is merged.

**Protecting `main` needs a bypass for GitHub Actions.** The bump job pushes its commit to `main` with `GITHUB_TOKEN`. `main` is unprotected today. If it gets branch protection or a ruleset, allow GitHub Actions to bypass it, or give the bump job a token that may push. A refused push fails the run before the tag exists, so nothing is left half-published.

## Unsigned, by decision

The zip and the exe aren't code-signed (design D6 of `add-packaging-ci`). On the first start, SmartScreen shows **Windows protected your PC**, and the user chooses **More info**, then **Run anyway**. Signing comes with the Velopack installer.

## The Visual C++ runtime

`transcribe.dll`, the `ggml*.dll` files and `onnxruntime.dll` need the Visual C++ runtime (`vcruntime140.dll`, `vcruntime140_1.dll`, `msvcp140.dll` and `msvcp140_1.dll`). A clean Windows doesn't have it, and a self-contained .NET app doesn't bring it. `build-zip.ps1` copies these files next to `Pisum.Transcribe.exe`, which Microsoft calls local deployment. Windows looks for a DLL's imports in the application folder first, so the copies are found on a machine without the Visual C++ Redistributable.

- **Which files:** the ones the payload imports, found with the guard's scan and repeated until nothing is missing, because the copied files import each other. A future package that needs another runtime file, such as `vcomp140.dll`, gets it without a change to the script.
- **Where from:** the newest `VC\Redist\MSVC\<version>\x64\Microsoft.VC14*.CRT\` folder of any Visual Studio instance that `vswhere -all -products *` finds, including Build Tools. A file that isn't in the CRT folder comes from its sibling folders, such as `Microsoft.VC143.OpenMP`. GitHub's `windows-latest` image has Visual Studio with the C++ tools. If no instance has the folder, the script copies the files from `System32` and warns that the zip isn't for publishing. In CI (`$env:CI` is `true`), that is an error, because the Visual Studio license terms cover redistributing the files from the redist folder.
- The script prints the source folder and the version of each file it copied, so a release's run log shows which runtime it shipped.

**The guard** reads the import table and the delay-load import table of every `.dll` and `.exe` in the folder, with a small PE reader in PowerShell. Every import named like a Visual C++ runtime file (`vcruntime*`, `msvcp*`, `concrt*`, `vcomp*`, `vccorlib*`) must be in the folder. If one is missing, the guard names it and every file that imports it, and fails. It doesn't check other Windows DLLs: the UCRT (`api-ms-win-crt-*`) is part of Windows 10 and later, and `vulkan-1.dll` comes with the GPU driver and is optional. Run on the build output in `src/Pisum.Transcribe/bin/`, the guard fails, because a build doesn't copy the runtime.

### Proving a zip on a clean machine

The guard knows only the Visual C++ runtime family, and the development machine and the runners have the Redistributable installed. So before a release that changes native dependencies, start the zip on a machine without .NET and without the Visual C++ Redistributable:

1. Enable Windows Sandbox once, as administrator, and restart:

   ```powershell
   Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All
   ```

2. In the sandbox, check that it is clean enough: `Test-Path C:\Windows\System32\vcruntime140.dll` must be `False`, and `dotnet` must not be found. If `vcruntime140.dll` exists, the sandbox can't prove anything about the runtime. Use a VM without the Redistributable instead.
3. Copy the zip into the sandbox, extract it, and start `Pisum.Transcribe.exe`. Download Canary 180M Flash (208 MB) in the setup window.
4. Check that the tray icon appears without a prompt to install a runtime, that the tooltip shows **Ready (CPU)** or **Ready (Vulkan)**, and that `%LOCALAPPDATA%\Pisum Transcribe\logs\` has `Pisum Transcribe <version>+<sha> starting` and "Voice activity detection is ready".

## ONNX Runtime notices

`third-party/onnxruntime-ThirdPartyNotices.txt` is a byte-for-byte copy of the `ThirdPartyNotices.txt` in the `Microsoft.ML.OnnxRuntime` package, and `third-party/.gitattributes` keeps its line endings. It belongs to the exact version pinned in `Directory.Packages.props`. When that pin changes, copy the new package's file over it, for example from `%NUGET_PACKAGES%\microsoft.ml.onnxruntime\<version>\ThirdPartyNotices.txt`.
