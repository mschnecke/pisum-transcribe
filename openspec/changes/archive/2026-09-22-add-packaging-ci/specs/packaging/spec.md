## Purpose

Defines how the built application becomes a versioned download on the project's GitHub Releases page, what that download contains, and how continuous integration checks every change before it can be released.

## ADDED Requirements

### Requirement: Self-contained zip for Windows x64
Each release SHALL provide one zip archive for Windows x64. The archive SHALL contain the application and every runtime dependency it needs, so that the application starts, detects speech and transcribes on a Windows 10 or 11 x64 machine that has neither a .NET runtime nor the Microsoft Visual C++ Redistributable installed. A GPU driver is not such a dependency: without Vulkan, the application runs on the CPU as the transcription spec describes. The archive SHALL contain exactly one top-level folder, `Pisum Transcribe`, which holds the application. The file name SHALL contain the release version and identify the platform, in the form `Pisum.Transcribe_<version>_win-x64.zip`.

#### Scenario: Start on a clean machine
- **WHEN** the archive is extracted on a Windows 11 x64 machine with neither a .NET runtime nor the Visual C++ Redistributable installed, and `Pisum.Transcribe.exe` in the extracted folder is started
- **THEN** the application reaches the tray without asking for a runtime to be installed

#### Scenario: Engine and silence trimming load on a clean machine
- **WHEN** on such a machine a speech model is downloaded in the setup window
- **THEN** the tray tooltip shows that the application is ready, on the CPU or on Vulkan
- **AND** the log shows that voice activity detection is ready

#### Scenario: Extracting creates one folder
- **WHEN** the archive is extracted with Windows Explorer's "Extract All"
- **THEN** the destination contains one folder, `Pisum Transcribe`, and no other files

#### Scenario: File name carries version and platform
- **WHEN** the assets of the release for version `0.1.0` are listed
- **THEN** the archive is named `Pisum.Transcribe_0.1.0_win-x64.zip`

### Requirement: The zip ships the license notices
The application folder in the archive SHALL contain the project's license and a third-party notices file. The notices file SHALL name every third-party component whose code the archive contains and SHALL include the license text or notice that the component's license requires. Speech models are not in the archive and are not covered by this requirement.

#### Scenario: Notices are next to the application
- **WHEN** the archive is extracted
- **THEN** the `Pisum Transcribe` folder contains the project's license and the third-party notices file

#### Scenario: A shipped native component has its notice
- **WHEN** the archive contains a component's library, such as the transcription engine or its compute libraries
- **THEN** the third-party notices file contains that component's name and license text

### Requirement: One version per release
A release SHALL carry one version, taken from its tag with the leading `v` removed. The release name, the archive file name and the version the application writes to its log at start SHALL all show that version. The repository SHALL record the version of the most recent release, and a build that is not a release SHALL report that recorded version.

#### Scenario: Versions agree
- **WHEN** the release for tag `v0.2.0` is published, and the application from its archive is started
- **THEN** the release name and the archive file name contain `0.2.0`
- **AND** the log entry with the application version written at start shows `0.2.0`

### Requirement: Publishing a release from a version tag
When a tag of the form `v<major>.<minor>.<patch>`, optionally followed by a pre-release suffix such as `-rc.1`, is pushed to the repository, a release for that tag SHALL be published on the project's GitHub Releases page with the archive attached. If any step before publishing fails, no release SHALL be published for that tag.

#### Scenario: Tag push publishes a release
- **WHEN** the tag `v0.2.0` is pushed
- **THEN** a release named for version `0.2.0` appears on the Releases page with `Pisum.Transcribe_0.2.0_win-x64.zip` attached

#### Scenario: A failed build publishes nothing
- **WHEN** a version tag is pushed and building the archive fails
- **THEN** no release exists for that tag

### Requirement: Starting a release by hand
A maintainer SHALL be able to start a release by hand, choosing either a patch, minor or major bump of the recorded version, or an exact version. The run SHALL record the new version in the repository, create and push the matching tag, and publish the release in the same run. A bump from a pre-release version SHALL resolve to that version without its suffix, so `0.1.0-rc.1` followed by a patch bump gives `0.1.0`. If the tag for the new version already exists, the run SHALL fail without recording a version or publishing a release.

#### Scenario: Patch bump
- **WHEN** the recorded version is `0.1.0` and a maintainer starts a release with a patch bump
- **THEN** the repository records `0.1.1` in a new commit, the tag `v0.1.1` exists, and the release for `0.1.1` is published

#### Scenario: Bump from a pre-release
- **WHEN** the recorded version is `0.1.0-rc.1` and a maintainer starts a release with a patch bump
- **THEN** the release published is `0.1.0`

#### Scenario: Version already released
- **WHEN** a maintainer starts a release with an exact version whose tag already exists
- **THEN** the run fails, and no commit, tag or release is created

### Requirement: Pre-release versions publish pre-releases
A release whose version has a pre-release suffix SHALL be marked as a pre-release on the Releases page, so that it is not offered as the latest release. A release without a suffix SHALL NOT be marked as a pre-release.

#### Scenario: Release candidate
- **WHEN** the tag `v0.1.0-rc.1` is pushed
- **THEN** the release for `0.1.0-rc.1` is published and marked as a pre-release

#### Scenario: Final release
- **WHEN** the tag `v0.1.0` is pushed
- **THEN** the release for `0.1.0` is published and not marked as a pre-release

### Requirement: Only a tested build is released
Every release run SHALL build the solution and run the default test suite from the tagged commit before it builds the archive. If the build produces a warning or error, or any test fails, no release SHALL be published.

#### Scenario: Failing test blocks the release
- **WHEN** a version tag is pushed on a commit where a test in the default test run fails
- **THEN** no release exists for that tag, and the run shows the failing test

### Requirement: Continuous integration checks every change
Every pull request to `main` and every push to `main` SHALL be built and tested on Windows. The run SHALL fail if the build produces a warning or error, or if any test in the default test run fails. The run SHALL NOT need a microphone, a GPU, a downloaded speech model or the user's desktop, and the hardware tests, which need one of them or use the real clipboard, foreground window or keyboard input, SHALL NOT run.

#### Scenario: Pull request with a failing test
- **WHEN** a pull request to `main` contains a change that makes a test fail
- **THEN** the check for that pull request fails and shows the failing test

#### Scenario: Direct push to main
- **WHEN** a commit is pushed directly to `main`
- **THEN** a build and test run for that commit starts

#### Scenario: Hardware tests don't run
- **WHEN** the continuous integration run completes
- **THEN** none of the hardware tests was run
