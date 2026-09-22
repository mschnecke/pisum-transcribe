## Why

Pisum Transcribe has no CI and no release. The only way to use it is to clone the repository and build it with the .NET SDK, and nothing checks a change before it lands on `main`. The repository is now public on GitHub (`mschnecke/pisum-transcript`), which is the project's home from now on, so a released build is what a new user needs. GitHub's hosted Windows runners also remove the blocker the roadmap names for CI ("This needs a Windows runner").

## What Changes

- **Continuous integration:** every pull request to `main` and every push to `main` is built and tested on a GitHub-hosted Windows runner. The default test run is already hermetic: the hardware tests are explicit and don't run.
- **Release:** a release is published to GitHub Releases in one of two ways, the same two as in `pisum-whisper`:
  - pushing a `v*` tag, or
  - running the **Release** workflow by hand, which bumps the version (patch, minor, major or an exact version), commits it, tags it and publishes in the same run.
  - A tag with a pre-release suffix (`v0.1.0-rc.1`) publishes a GitHub pre-release.
- **The artifact:** a self-contained zip, `Pisum.Transcribe_<version>_win-x64.zip`. It holds one `Pisum Transcribe\` folder that runs on a machine with neither .NET nor the Visual C++ Redistributable installed. It contains the license notices and is unsigned.
- **The Visual C++ runtime ships in the zip:** the transcription engine and ONNX Runtime need it, and a clean Windows doesn't have it. Neither the development machine nor CI would notice it's missing, because both have it installed. A check in the packaging script fails the build when a native library in the zip imports a Visual C++ runtime file that the zip doesn't contain.
- **One version:** `Directory.Build.props` gets a `<Version>`, and `packaging/bump-version.sh` from `pisum-whisper` maintains it. A release takes its version from the tag, so the zip name, the release and the version the app logs at start agree.
- **Tests gate the release:** the release run runs the tests before it builds the zip. `main` is unprotected and gets direct pushes, so a tag can point at a commit that CI never checked.
- **Third-party notices:** `THIRD-PARTY-NOTICES.md` lists every third-party component the zip ships, not only Silero VAD and ONNX Runtime.
- **GitHub is the home:** `README.md`, `CLAUDE.md` and `docs/roadmap.md` point to GitHub instead of GitLab. The README's *Getting started* section starts with downloading the zip.
- Not included:
  - an installer, auto-update and a Start menu entry (Velopack is a later change)
  - code signing
  - WinGet and Chocolatey packages
  - the old app's package channels. An earlier app called "Pisum Transcript" used this repository's name. Its Homebrew tap (`mschnecke/homebrew-pisum-transcript`) and its Chocolatey package on MyGet (`pisum-transcript`) still point to release URLs in this repository that no longer exist. Neither can install this app by accident. They should be retired before the first release, but outside this change. When this app gets a Chocolatey package, its id is `pisum-transcribe`.

## Capabilities

### New Capabilities
- `packaging`: how the built application becomes a versioned, downloadable release: the zip, its contents and version, how a release is published, and the CI that checks every change.

### Modified Capabilities
<!-- None. The app-shell scenario that logs the application version at start is unchanged; this change only gives it a real version. -->

## Impact

- New files:
  - `.github/workflows/ci.yml` and `.github/workflows/release.yml`
  - `packaging/bump-version.sh`, `packaging/windows/build-zip.ps1`, `packaging/windows/assert-native-dependencies.ps1`, `packaging/windows/.gitignore` and `packaging/README.md`
  - `packaging/third-party/onnxruntime-ThirdPartyNotices.txt`
- Changed files:
  - `Directory.Build.props`: a `<Version>`, seeded as `0.0.0`
  - `Directory.Packages.props`: the ONNX Runtime pin comment says to refresh its notices file
  - `THIRD-PARTY-NOTICES.md`: completed
  - `README.md`, `CLAUDE.md`, `docs/roadmap.md`: GitHub instead of GitLab, download and release instructions, and the roadmap's deferred packaging item
- No change to `src/` or `tests/`, unless the first CI run shows a test that fails on the runner. Such a test skips itself when the capability it needs is missing (see design D7).
- No new package dependencies. The workflows use `actions/checkout`, `actions/setup-dotnet`, `actions/upload-artifact`, `actions/download-artifact` and `softprops/action-gh-release`, as `pisum-whisper` does.
- No repository secrets are needed: the workflows use only `GITHUB_TOKEN`.
- User-visible: a download on GitHub Releases that needs nothing else installed. The unsigned exe shows a SmartScreen warning on first start.
