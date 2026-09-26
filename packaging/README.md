# packaging

Everything that turns a build into a release. Nothing here is compiled into the app or read at run time, and `dotnet build` and `dotnet test` never look in this folder. Only a macOS publish reads `third-party/`, whose notices the project's bundle target copies into the release bundle.

| Path | What it is |
|---|---|
| `bump-version.sh` | Decides the version of the next release and writes it into `Directory.Build.props` |
| `windows/build-msi.ps1` | Publishes the app and builds and validates `artifacts/Pisum.Transcribe_<version>_win-x64.msi` |
| `windows/Pisum.Transcribe.wxs` | The MSI's WiX source |
| `windows/assert-native-dependencies.ps1` | The guard: fails when a library in a folder imports a Visual C++ runtime file that the folder doesn't contain, or when a native library of the Avalonia shell is missing |
| `windows/.gitignore` | Keeps `windows/publish/`, where `build-msi.ps1` assembles the app folder, untracked |
| `macos/build-pkg.sh` | Publishes the app bundle, runs the guard on it, and builds `artifacts/Pisum.Transcribe_<version>_osx-arm64.pkg` |
| `macos/assert-bundle.sh` | The guard of the Mac release: fails when the bundle isn't signed with the project's certificate, has a Mach-O file that isn't arm64 only or needs a macOS newer than 14.0, contains a native Windows or Linux file, or carries another version |
| `macos/Distribution.xml` | The package's distribution, a template with `@VERSION@`: Apple silicon, macOS 14, `/Applications`, and no older version over a newer one |
| `macos/preinstall`, `macos/postinstall` | The package's scripts: quit a running app before the installation, remove the quarantine and start the app after it |
| `third-party/onnxruntime-ThirdPartyNotices.txt` | ONNX Runtime's notices for the components it bundles, installed as `ThirdPartyNotices-OnnxRuntime.txt` |
| `third-party/dotnet-runtime-THIRD-PARTY-NOTICES.txt` | The .NET runtime's notices for the components it bundles, installed as `ThirdPartyNotices-DotNet.txt` |
| `third-party/avalonia-NOTICE.txt` | Avalonia's notices for the code it contains from other projects, installed as `ThirdPartyNotices-Avalonia.txt` |
| `third-party/skiasharp-THIRD-PARTY-NOTICES.txt` | SkiaSharp's and HarfBuzzSharp's notices for Skia, HarfBuzz and the libraries in `libSkiaSharp.dll`, installed as `ThirdPartyNotices-SkiaSharp.txt` |

WiX itself is pinned in `.config/dotnet-tools.json` at the repository root (see [The pins](#the-pins)).

## The scripts

Each script takes one argument. A person runs the same command as the workflows, so nothing about a release exists only in `.github/workflows/`.

```sh
./packaging/bump-version.sh patch                                    # Git Bash: 0.1.0 -> 0.1.1, prints 0.1.1
./packaging/windows/build-msi.ps1 -Version 0.1.0-dev.1               # PowerShell 7: the MSI, in artifacts\
./packaging/windows/assert-native-dependencies.ps1 -Path <folder>    # PowerShell 7: the guard on any folder
./packaging/macos/build-pkg.sh 1.4.0-dev.1                           # macOS: the package, in artifacts/
./packaging/macos/assert-bundle.sh <bundle.app> <version>            # macOS: the guard on any bundle
```

The macOS scripts take options for a test package that isn't signed with the project's certificate (see [The macOS package](#the-macos-package)).

`build-msi.ps1` does this, and stops at the first error:

1. It publishes `src/Pisum.Transcribe` self-contained and ReadyToRun into `windows/publish/Pisum Transcribe/`, with the version passed as `-p:Version`. It isn't single-file and isn't trimmed (design D2 of `add-packaging-ci`).
2. It deletes the `*.lib` import libraries, which nothing loads at run time, and `libSkiaSharp.pdb` and `libHarfBuzzSharp.pdb`, the native debug symbols of the Skia and HarfBuzz packages (about 100 MB).
3. It writes the project's `LICENSE` over the one from the transcribe.cpp native package. That package ships three license files, which NuGet flattens into one `LICENSE`. `dotnet publish` refuses that (NETSDK1152), so the script passes `-p:ErrorOnDuplicatePublishOutputFiles=false`. The three texts are in `THIRD-PARTY-NOTICES.md`.
4. It copies `THIRD-PARTY-NOTICES.md` and the notices of ONNX Runtime, .NET, Avalonia and SkiaSharp into the folder.
5. It copies the Visual C++ runtime files that the payload imports (see below).
6. It runs the guard on the folder.
7. It restores WiX, builds `windows/Pisum.Transcribe.wxs` from the folder into the MSI, and validates the MSI (see [The MSI](#the-msi)).

The MSI is about 67 MB. It installs about 227 MB in 288 files, most of it `ggml-vulkan.dll`, `onnxruntime.dll`, the .NET runtime and the Avalonia shell with Skia. `Pisum.Transcribe.pdb` is installed too, so a logged stack trace has line numbers. Building the MSI takes about a minute after publishing, and validating it a few seconds.

## The macOS package

`build-pkg.sh <version> [--identity <name>] [--leaf <sha1> | --adhoc]` does this, and stops at the first error:

1. It publishes `src/Pisum.Transcribe` for `osx-arm64` into `macos/publish/`, self-contained and ReadyToRun, with the same flags as `build-msi.ps1` and the signing identity as `-p:PisumCodesignIdentity` (design D1 of `add-macos-packaging`). After the publish, the project's bundle target assembles `macos/publish/Pisum Transcribe.app` from the publish output, the same target that assembles the dev bundle after a build. For the release bundle, it also removes the flattened `LICENSE` from `Contents/MacOS/`, puts the project's `LICENSE`, `THIRD-PARTY-NOTICES.md` and the notices of ONNX Runtime, .NET, Avalonia and SkiaSharp into `Contents/Resources/` under the MSI's names, and thins the universal libraries of SkiaSharp, HarfBuzzSharp and Avalonia.Native to their `arm64` slice. Then it signs the bundle inside-out.
2. It runs the guard, `assert-bundle.sh`, on the bundle.
3. It builds the component package with `pkgbuild`: the bundle into `/Applications`, with `preinstall` and `postinstall` and nothing else from this folder. The component plist from `pkgbuild --analyze` gets `BundleIsRelocatable = false`, so Installer never updates another copy of the app with the same bundle identifier, such as the dev bundle in `bin/`, and `BundleIsVersionChecked = false`, so `Distribution.xml` alone decides about versions.
4. It builds the product archive with `productbuild` from a copy of `Distribution.xml` with the version filled in. Neither package is signed.

The bundle is about 160 MB in about 260 files, and the package about 56 MB.

**Options.** The default identity is the project's certificate, **Pisum Transcribe**, which only the release job has. A developer builds a test package with their own identity and tells the guard its fingerprint, or signs ad hoc:

```sh
./packaging/macos/build-pkg.sh 1.4.0-dev.1 --identity "Pisum Transcribe Development" --leaf <sha1>
./packaging/macos/build-pkg.sh 1.4.0-dev.1 --adhoc
```

`security find-identity -p codesigning` lists the identities with their SHA-1 fingerprints. An ad hoc package loses its permissions with every update.

### The guard

`assert-bundle.sh <bundle.app> <version> [--leaf <sha1> | --adhoc]` checks every file of the bundle and names each failure:

- **The signature:** `codesign --verify --strict` passes, and the designated requirement names the project's certificate, `certificate leaf = H"a2eca9bd0a5157e33a43160ed01c502c6d86980b"`, or the one given with `--leaf`. A release signed with another certificate would cost every user their permissions.
- **Mach-O files:** `arm64` only (`lipo -archs`), and a minimum macOS (`minos` from `vtool -show-build`) of 14.0 or older. An ONNX Runtime update that raises its minimum fails here.
- **No native file of another platform:** no PE file without a CLI header, as `file` reports it, and no ELF file. .NET assemblies are PE files too and pass.
- **The version:** the bundle's `CFBundleShortVersionString` is the version given.

### Installing, upgrading and removing

- **Checks before the installation:** `Distribution.xml` allows only Apple silicon (`hostArchitectures="arm64"`) and macOS 14 or later, and installs only into `/Applications`, for all users. Its `installation-check` reads the version of an installed `/Applications/Pisum Transcribe.app` and refuses the package when that version, without its pre-release suffix, is higher than the package's: **A newer version of Pisum Transcribe is already installed.** Equal versions pass, so a release installs over its release candidates and the other way round, like the MSI.
- **`preinstall`** ends every running instance of `/Applications/Pisum Transcribe.app`, of every logged-in user, with `SIGTERM`, which the app handles like **Quit Pisum Transcribe**. It waits up to 6 seconds and then sends `SIGKILL`. It matches the executable's path, so a dev build elsewhere keeps running. It never fails the installation.
- **`postinstall`** removes the quarantine from the app, and opens it for the user at the screen, unless the installation ran from the command line (`installer` and Homebrew set `COMMAND_LINE_INSTALL`). Instances of other users that `preinstall` ended stay ended until those users open the app again.
- **Removing:** moving the app to the Trash, as the README describes. There is no uninstaller. The receipt `io.github.mschnecke.pisum-transcribe` stays until `sudo pkgutil --forget io.github.mschnecke.pisum-transcribe`.

`installer -pkg <package> -target / -verbose` in a terminal, or **Window** > **Installer Log** in Installer, shows what the scripts and the checks did.

### The signing certificate

The app in every release is signed with the project's own self-signed certificate **Pisum Transcribe**, an RSA 3072-bit code-signing certificate valid for 20 years, with the SHA-1 fingerprint `A2:EC:A9:BD:0A:51:57:E3:3A:43:16:0E:D0:1C:50:2C:6D:86:98:0B`. macOS ties the Accessibility and microphone permissions to the designated requirement, which names that certificate, so they survive updates. There is no Developer ID and no notarization (design D5 of `add-macos-packaging`).

- **Where it lives:** as the repository secrets `MACOS_CERTIFICATE_P12`, the `.p12` in base64, and `MACOS_CERTIFICATE_PASSWORD`, and in a backup outside GitHub that the maintainer keeps. It isn't in any keychain in daily use. The release job imports it into a temporary keychain and deletes that keychain at the end, also after a failure.
- **Losing it** means every user grants Accessibility and the microphone once more after the next update, signed with a new certificate. Restore it from the backup instead.
- **Replacing it** (a new certificate): change the fingerprint in `macos/assert-bundle.sh`, the secrets and this section, and say in the release notes that users grant the permissions again.

## Versions

`Directory.Build.props` holds the version of the last release, in `<Version>`. A build that isn't a release reports that version. A release takes its version from the tag, without the `v`, and passes it to `build-msi.ps1`, so the MSI name, the release name and the version the app logs at start agree. Windows' list of installed apps shows the version without its pre-release suffix (see [Upgrades](#upgrades)).

`bump-version.sh` reads `<Version>`, writes the next one and prints it. It doesn't touch git: the edit is one line that `git diff` shows and `git checkout` undoes.

```sh
./packaging/bump-version.sh patch        # 0.1.0 -> 0.1.1
./packaging/bump-version.sh minor        # 0.1.0 -> 0.2.0
./packaging/bump-version.sh 0.2.0-rc.1   # an exact version, for a pre-release
```

- **A keyword bump from a pre-release drops the suffix.** `0.2.0-rc.1` was a rehearsal for `0.2.0`, so `patch` gives `0.2.0`, not `0.2.1`. For a further pre-release, pass the exact version.
- **It refuses to write the version the file already has.** That is why the seed is `0.0.0`, not `0.1.0`: the first release is the exact version `0.1.0-rc.1`, and the `patch` after it gives `0.1.0`.

## The MSI

`windows/Pisum.Transcribe.wxs` is the whole package. `build-msi.ps1` passes it three values: `Version`, and `PublishDir` and `IconFile` as absolute paths. They are absolute because WiX resolves a `SourceFile` against the current directory and a `<Files>` pattern against the current directory plus the `Directory` names around it, never against the `.wxs` file. `<Files>` takes the published folder whole, one component per file, so there is no harvesting step and no component list to keep in sync.

**Validation.** `wix build` doesn't run the ICE validation, so the script runs `wix msi validate -sice ICE61 -wx` after it. `-wx` turns every warning into an error. `-sice ICE61` suppresses the one expected warning: ICE61 objects to an upgrade that also replaces the same version, which is how a final release replaces its release candidates (see [Upgrades](#upgrades)). A failed validation fails the script, and with it the CI run or the release.

### Per user, without administrator rights

The package is dual-purpose (`Scope="perUserOrMachine"`), which Microsoft calls single package authoring. It sets `ALLUSERS=2` and `MSIINSTALLPERUSER=1`, so opening it installs for the current user. Its files go under `ProgramFiles64Folder`, which Windows Installer redirects to `%LOCALAPPDATA%\Programs\` in a per-user install, so the app lands in `%LOCALAPPDATA%\Programs\Pisum Transcribe\`. The Start Menu shortcut goes into the user's Start Menu.

- **Not `Scope="perUser"` with the files under `LocalAppDataFolder`:** validation then fails with ICE38 and ICE64 errors and ICE91 warnings for the files, because each file lies in the user profile and is its own key path. Suppressing three rules would also hide their real findings.
- **The shortcut's key path is `HKCU\Software\Pisum\Transcribe`.** `HKMU` fails ICE57, because validation counts the Start Menu as per-user data.
- **Only the per-user install is supported and tested.** A per-machine install (`ALLUSERS=1`) isn't.

The spike on 2026-09-22 installed the package by opening it: Windows Installer registered it per user (`AssignmentType` 0), and put the files into `%LOCALAPPDATA%\Programs\Pisum Transcribe\` and the shortcut into `%APPDATA%\Microsoft\Windows\Start Menu\Programs\`. On a VM with UAC on, opening the `0.1.0-rc.2` MSI as a user without an elevated session showed no UAC prompt, and Task Manager showed the app it started with **Elevated** set to **No**. So the dual-purpose package needs no fallback to `Scope="perUser"`.

**Starting the app.** A tray app shows nothing after a plain install, so an interactive install starts the app when it finishes: a `WixShellExec` custom action after `InstallFinalize` runs `[INSTALLFOLDER]Pisum.Transcribe.exe` as the user who opened the package. Its condition is `UILevel = 5 AND NOT Installed AND NOT REMOVE`. Opening the package gives `UILevel` 5, even though it has no dialogs of its own. `msiexec /qn` and `/passive`, as a package manager runs them, give 2 and 3 and don't start the app. A failed start doesn't fail the install.

### Upgrades

- **Version:** the MSI's `ProductVersion` is the numeric core of the release version, because a Windows Installer version has no pre-release part. `build-msi.ps1` strips everything from the first `-`. The file name keeps the full version. Every build gets a new `ProductCode`, and the `UpgradeCode` never changes.
- **Same-version upgrades:** `<MajorUpgrade AllowSameVersionUpgrades="yes">` lets `0.1.0` replace `0.1.0-rc.2`, because both are `0.1.0` to Windows Installer. The price is that `0.1.0-rc.3` would also replace an installed `0.1.0`. Pre-releases are rehearsals, so that is accepted.
- **The schedule must stay `afterInstallValidate`,** the default, which removes the old version completely before it installs the new one. File versions don't carry the pre-release suffix: the SDK gives `Pisum.Transcribe.dll` the file version `0.1.0.0` for `0.1.0-rc.2` and for `0.1.0` alike, and the native DLLs have no version. Windows Installer doesn't replace a file with one of the same version, so with a later schedule such as `afterInstallExecute`, `0.1.0` would keep rc.2's binaries.
- **Going back means uninstalling first.** An older version is a downgrade to Windows Installer, and the package refuses it with **A newer version of Pisum Transcribe is already installed.** That includes going back from a release candidate to an older stable release: `0.1.1-rc.1` → `0.1.0`.
- **A running app** is closed by Windows Installer's Restart Manager. On an interactive upgrade or uninstall, Windows Installer shows its own prompt, **The following applications should be closed before continuing the install:**, which lists `Pisum.Transcribe`. **OK** closes the app with the end-of-session messages (`WM_QUERYENDSESSION` and `WM_ENDSESSION` with `ENDSESSION_CLOSEAPP`). The app handles them as the end of the Windows session and ends as at **Exit**; its log shows `Shutting down, reason "SessionEnd"`. In the spike, the app ended about 10 seconds after **OK**, and no restart of Windows was needed. With `/qn`, Restart Manager closes the app without asking.
- **What stays:** the MSI never owns the data folder or the "Start with Windows" entry, so an upgrade keeps the settings, the models, the logs and that entry. The install folder stays the same, so the entry still points at the right exe.

### Uninstall cleanup

The app, not the MSI, owns "Start with Windows": the value `Pisum Transcribe` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, and the value of the same name that Task Manager writes under `…\Explorer\StartupApproved\Run` to disable it. On uninstall, two deferred, impersonated `WixQuietExec64` custom actions delete both values with `reg.exe`. The app also owns its notification registration, the key `HKCU\Software\Classes\AppUserModelId\Pisum.Transcribe` that it writes at every start, and a third custom action of the same kind deletes that key.

- They run only on a real uninstall (`REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE`), so an upgrade keeps the entry, the key and the user's notification setting for the app.
- They ignore failures: `reg.exe` exits with 1 when a value or key doesn't exist, and a failed cleanup must never block an uninstall.
- `%LOCALAPPDATA%\Pisum Transcribe\`, with the settings, the logs and the models, isn't the MSI's and stays. A reinstall uses it again.

### The pins

- **`wix`** is pinned exactly in the local tool manifest `.config/dotnet-tools.json`. `build-msi.ps1` runs `dotnet tool restore`, so WiX needs no global install.
- **`WixToolset.Util.wixext`**, the extension the custom actions come from, gets the same version: the script reads it from the manifest and adds the extension to WiX's per-user cache (`wix extension add -g`), so the two can't drift apart.
- **Moving to a new WiX version:** `dotnet tool update wix --version <x.y.z>`, then build and validate. Update the WiX Toolset section of `THIRD-PARTY-NOTICES.md` (its tag, commit and license text) and the WiX commit in `.github/workflows/release.yml` (see [Releasing](#releasing)), and re-read the terms below.
- **WiX's maintenance fee:** WiX's binaries come under the Open Source Maintenance Fee agreement, `OSMFEULA.txt` in the `wix` package. Its fee applies only to users that generate revenue with WiX, and this project doesn't. The source, including the custom action DLL embedded in the MSI, is under the MS-RL, whose notice is in `THIRD-PARTY-NOTICES.md`.

## Releasing

There are two ways to start a release, and both end the same way. The **Release** workflow (`.github/workflows/release.yml`) builds both installers in parallel from the tagged commit: `build-windows` runs the tests on Windows and builds the MSI, and `build-macos` runs the tests on macOS and builds the package, signed in a temporary keychain from the secrets. Only when both succeed does `release` publish them together on GitHub Releases with generated release notes, so a release never carries only one of them. If a test or a build fails on either platform, nothing is published. A version with a suffix, such as `0.2.0-rc.1`, is published as a pre-release.

Next to the installers, every release carries the source of the two copyleft components in them, downloaded from GitHub by the release job: `libuiohook-<commit>.tar.gz` (LGPL, inside `uiohook.dll` and `libuiohook.dylib`) and `wix-<commit>.tar.gz` (MS-RL, the custom action DLL embedded in the MSI). Their licenses ask for the source to come with the binaries, and a copy on the same release doesn't depend on the upstream repositories staying online. The commits are the ones `THIRD-PARTY-NOTICES.md` names; a new SharpHook or WiX version changes both places.

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

The MSI and the exe aren't code-signed (design D6 of `add-packaging-ci`). When the MSI is opened, SmartScreen shows **Windows protected your PC**, and the user chooses **More info**, then **Run anyway**. Signing is a separate change.

The macOS package isn't signed either, and the app inside is signed only with the project's own certificate, which Gatekeeper doesn't trust. A downloaded package therefore needs **Open Anyway** in **System Settings** > **Privacy & Security** once, as the README describes. `postinstall` removes the app's quarantine, so the installed app then opens without a further prompt.

## The Visual C++ runtime

`transcribe.dll`, the `ggml*.dll` files and `onnxruntime.dll` need the Visual C++ runtime (`vcruntime140.dll`, `vcruntime140_1.dll`, `msvcp140.dll` and `msvcp140_1.dll`). A clean Windows doesn't have it, and a self-contained .NET app doesn't bring it. `build-msi.ps1` copies these files next to `Pisum.Transcribe.exe`, which Microsoft calls local deployment. Windows looks for a DLL's imports in the application folder first, so the copies are found on a machine without the Visual C++ Redistributable.

- **Which files:** the ones the payload imports, found with the guard's scan and repeated until nothing is missing, because the copied files import each other. A future package that needs another runtime file, such as `vcomp140.dll`, gets it without a change to the script.
- **Where from:** the newest `VC\Redist\MSVC\<version>\x64\Microsoft.VC14*.CRT\` folder of any Visual Studio instance that `vswhere -all -products *` finds, including Build Tools. A file that isn't in the CRT folder comes from its sibling folders, such as `Microsoft.VC143.OpenMP`. GitHub's `windows-latest` image has Visual Studio with the C++ tools. If no instance has the folder, the script copies the files from `System32` and warns that the MSI isn't for publishing. In CI (`$env:CI` is `true`), that is an error, because the Visual Studio license terms cover redistributing the files from the redist folder.
- The script prints the source folder and the version of each file it copied, so a release's run log shows which runtime it shipped.

**The guard** reads the import table and the delay-load import table of every `.dll` and `.exe` in the folder, with a small PE reader in PowerShell. Every import named like a Visual C++ runtime file (`vcruntime*`, `msvcp*`, `concrt*`, `vcomp*`, `vccorlib*`) must be in the folder. If one is missing, the guard names it and every file that imports it, and fails. It doesn't check other Windows DLLs: the UCRT (`api-ms-win-crt-*`) is part of Windows 10 and later, and `vulkan-1.dll` comes with the GPU driver and is optional. It also fails when `libSkiaSharp.dll`, `libHarfBuzzSharp.dll` or `av_libglesv2.dll` is missing. Avalonia loads them by name at run time, so no import table names them. Run on the build output in `src/Pisum.Transcribe/bin/`, the guard fails, because a build doesn't copy the runtime.

## Proving an MSI

Validation checks the package's tables, not what an installation does. Before a release that changes the package, prove it twice.

### Without administrator rights

On a machine where User Account Control is on (with UAC off, every process of an administrator runs elevated, and this check proves nothing), open the MSI as a user whose session isn't elevated. `msiexec /i <msi> /l*v install.log` gives the same install with a log. Check that:

1. no UAC prompt appears,
2. the files are in `%LOCALAPPDATA%\Programs\Pisum Transcribe\`,
3. the app starts when the installation finishes and shows its tray icon, and Task Manager's **Details** tab shows it with **Elevated** set to **No** (add the column with a right-click on the header),
4. the Start Menu has **Pisum Transcribe**, which also starts the app,
5. Windows Settings → **Apps** shows **Pisum Transcribe** with its icon and the version without the pre-release suffix.

### On a clean machine

The guard knows only the Visual C++ runtime family, and the development machine and the runners have the Redistributable installed. So before a release that changes native dependencies, install the MSI on a machine without .NET and without the Visual C++ Redistributable:

1. Enable Windows Sandbox once, as administrator, and restart:

   ```powershell
   Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All
   ```

2. In the sandbox, check that it is clean enough: `Test-Path C:\Windows\System32\vcruntime140.dll` must be `False`, and `dotnet` must not be found. If `vcruntime140.dll` exists, the sandbox can't prove anything about the runtime. Use a VM without the Redistributable instead.
3. Copy the MSI into the sandbox and open it. The app starts when the installation finishes. Download Canary 180M Flash (208 MB) in the setup window.
4. Check that the tray icon appears without a prompt to install a runtime, that the tooltip shows **Ready (CPU)** or **Ready (Vulkan)**, and that `%LOCALAPPDATA%\Pisum Transcribe\logs\` has `Pisum Transcribe <version>+<sha> starting` and "Voice activity detection is ready".

## Notices of bundled components

The files in `third-party/` are byte-for-byte copies of the notices that ONNX Runtime, .NET, Avalonia and SkiaSharp publish for the components they bundle, and `third-party/.gitattributes` keeps their line endings. Each belongs to one version, so refresh it when that version changes:

- **`onnxruntime-ThirdPartyNotices.txt`** is the `ThirdPartyNotices.txt` in the `Microsoft.ML.OnnxRuntime` package, of the exact version pinned in `Directory.Packages.props`. When that pin changes, copy the new package's file over it, for example from `%NUGET_PACKAGES%\microsoft.ml.onnxruntime\<version>\ThirdPartyNotices.txt`.
- **`dotnet-runtime-THIRD-PARTY-NOTICES.txt`** is the `THIRD-PARTY-NOTICES.TXT` in the `Microsoft.NETCore.App.Runtime.win-x64` package, of the runtime version that a self-contained publish ships. The SDK pinned in `global.json` decides that version; `Pisum.Transcribe.deps.json` in the publish folder names it (`runtimepack.Microsoft.NETCore.App.Runtime.win-x64/<version>`). When it changes, copy the file from `%NUGET_PACKAGES%\microsoft.netcore.app.runtime.win-x64\<version>\THIRD-PARTY-NOTICES.TXT`.
- **`avalonia-NOTICE.txt`** is `NOTICE.md` of AvaloniaUI/Avalonia at the commit that the `Avalonia` package's `.nuspec` names in its `repository` element. The package has no notices file, so it comes from GitHub:

  ```sh
  # Git Bash, whose redirection keeps the bytes as GitHub serves them
  gh api -H 'Accept: application/vnd.github.raw' 'repos/AvaloniaUI/Avalonia/contents/NOTICE.md?ref=<commit>' > packaging/third-party/avalonia-NOTICE.txt
  ```

- **`skiasharp-THIRD-PARTY-NOTICES.txt`** is the `THIRD-PARTY-NOTICES.txt` in the `SkiaSharp.NativeAssets.Win32` package, of the version that Avalonia.Skia pulls in (`Pisum.Transcribe.deps.json` names it). The `HarfBuzzSharp.NativeAssets.Win32` package carries the same file. When the version changes, copy it from `%NUGET_PACKAGES%\skiasharp.nativeassets.win32\<version>\THIRD-PARTY-NOTICES.txt`.

After a refresh, update the versions in the matching section of `THIRD-PARTY-NOTICES.md`.
