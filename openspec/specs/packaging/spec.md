# packaging Specification

## Purpose

Defines how the built application becomes a versioned download on the project's GitHub Releases page, what that download contains, how it installs, upgrades and uninstalls, and how continuous integration checks every change before it can be released.

## Requirements

### Requirement: Windows installer for x64
Each release SHALL provide one Windows Installer package (MSI) for Windows x64. Installing it SHALL NOT require administrator rights: it SHALL install the application for the current user only, into `%LOCALAPPDATA%\Programs\Pisum Transcribe\`, and SHALL add a Start Menu shortcut named "Pisum Transcribe" and an entry in Windows' list of installed apps. The package SHALL contain the application and every runtime dependency it needs, so that the installed application starts, detects speech and transcribes on a Windows 10 x64 machine of version 2004 or later, or a Windows 11 x64 machine, that has neither a .NET runtime nor the Microsoft Visual C++ Redistributable installed, without installing anything else first. A GPU driver is not such a dependency: without Vulkan, the application runs on the CPU as the transcription spec describes. When the package is installed interactively, by opening it, the installer SHALL start the application, without administrator rights, when it finishes. An unattended installation, such as `msiexec /qn`, SHALL NOT start it. The file name SHALL contain the release version and identify the platform, in the form `Pisum.Transcribe_<version>_win-x64.msi`.

#### Scenario: Install without administrator rights
- **WHEN** a user who isn't an administrator opens the package
- **THEN** the application is installed into `%LOCALAPPDATA%\Programs\Pisum Transcribe\` without asking for administrator credentials
- **AND** the Start Menu has a "Pisum Transcribe" shortcut, and Windows' list of installed apps shows "Pisum Transcribe"

#### Scenario: Opening the package starts the application
- **WHEN** the package is opened and the installation finishes
- **THEN** the application starts without administrator rights and shows its tray icon
- **AND** without a downloaded speech model, it opens the setup window

#### Scenario: An unattended installation doesn't start the application
- **WHEN** the package is installed with `msiexec /i <package> /qn`
- **THEN** the installation completes and the application isn't running

#### Scenario: Start on a clean machine
- **WHEN** the package is installed on a Windows 11 x64 machine with neither a .NET runtime nor the Visual C++ Redistributable installed, and the application is started from its Start Menu shortcut
- **THEN** the application reaches the tray without asking for a runtime to be installed

#### Scenario: Engine and silence trimming load on a clean machine
- **WHEN** on such a machine a speech model is downloaded in the setup window
- **THEN** the tray tooltip shows that the application is ready, on the CPU or on Vulkan
- **AND** the log shows that voice activity detection is ready

#### Scenario: File name carries version and platform
- **WHEN** the assets of the release for version `0.1.0` are listed
- **THEN** the installer is named `Pisum.Transcribe_0.1.0_win-x64.msi`

### Requirement: Upgrading in place
Installing the package of a release while another version is installed SHALL replace the installed application in its folder, so that exactly one copy stays installed. A package whose version differs from the installed one only by a pre-release suffix SHALL also replace it, so that a final release installs over its release candidates. A package whose version, without its pre-release suffix, is lower than the installed one SHALL be refused with a message, and the installed application SHALL stay unchanged. An upgrade SHALL keep the user's settings, logs, downloaded speech models and "Start with Windows" entry. If the application is running, it SHALL end as it does when the user chooses **Exit** before its files are replaced, and the upgrade SHALL NOT need a restart of Windows. After an interactive upgrade, the installer SHALL start the new version, as after an interactive installation.

#### Scenario: Newer release over an older one
- **WHEN** the package of `0.2.0` is installed while `0.1.0` is installed, and the application is started afterwards
- **THEN** one copy is installed, and the log entry with the application version written at start shows `0.2.0`
- **AND** the settings, the downloaded models and the "Start with Windows" setting are as they were before the upgrade

#### Scenario: Final release over its release candidate
- **WHEN** the package of `0.1.0` is installed while `0.1.0-rc.2` is installed
- **THEN** `0.1.0` replaces it, and one copy is installed

#### Scenario: Older release refused
- **WHEN** the package of `0.1.0` is opened while `0.2.0` is installed
- **THEN** the installer shows a message that a newer version is installed, and `0.2.0` stays installed

#### Scenario: Upgrade while the application runs
- **WHEN** the package of a newer release is installed while the application runs, and the user agrees to close applications if the installer asks
- **THEN** the application ends as it does at **Exit**, the upgrade completes without a restart of Windows, and the new version starts when the upgrade finishes

### Requirement: Uninstalling
Uninstalling the application through Windows' list of installed apps SHALL remove the application's program folder, its Start Menu shortcut, its "Start with Windows" entry, including an entry the user disabled in Task Manager, and its notification registration, the per-user registry key `HKCU\Software\Classes\AppUserModelId\Pisum.Transcribe`. It SHALL keep the per-user data folder `%LOCALAPPDATA%\Pisum Transcribe\` with the settings, the logs and the downloaded speech models. An upgrade SHALL NOT remove the "Start with Windows" entry or the notification registration. If the application is running, it SHALL end as it does when the user chooses **Exit** before its files are removed.

#### Scenario: Uninstall removes the program
- **WHEN** the application is uninstalled
- **THEN** `%LOCALAPPDATA%\Programs\Pisum Transcribe\` holds none of the application's files, the Start Menu has no "Pisum Transcribe" shortcut, and Windows' list of installed apps doesn't show it

#### Scenario: Uninstall removes the startup entry
- **WHEN** "Start with Windows" is on and the application is uninstalled
- **THEN** Windows' list of startup apps no longer shows Pisum Transcribe, and nothing tries to start it at the next sign-in

#### Scenario: Uninstall removes the notification registration
- **WHEN** the application has shown a notification and is then uninstalled
- **THEN** the registry key `HKCU\Software\Classes\AppUserModelId\Pisum.Transcribe` no longer exists

#### Scenario: An upgrade keeps the notification registration
- **WHEN** a newer package is installed over an installed version
- **THEN** the registry key `HKCU\Software\Classes\AppUserModelId\Pisum.Transcribe` still exists, and the user's notification setting for Pisum Transcribe is unchanged

#### Scenario: Uninstall keeps the user's data
- **WHEN** the application is uninstalled and the package is installed again
- **THEN** `settings.json`, the logs and the downloaded speech models are still in `%LOCALAPPDATA%\Pisum Transcribe\`
- **AND** the reinstalled application starts with those settings and loads the selected model without downloading it again

#### Scenario: Uninstall while the application runs
- **WHEN** the application is uninstalled while it runs, and the user agrees to close applications if Windows asks
- **THEN** the application ends as it does at **Exit**, and the uninstall completes without a restart of Windows

### Requirement: The installed application ships the license notices
The application folder that the installer creates SHALL contain the project's license and a third-party notices file. The notices file SHALL name every third-party component whose code the installer package contains, including code that runs only while the package is installed or uninstalled, and SHALL include the license text or notice that the component's license requires. Speech models are not in the package and are not covered by this requirement.

#### Scenario: Notices are next to the application
- **WHEN** the package is installed
- **THEN** `%LOCALAPPDATA%\Programs\Pisum Transcribe\` contains the project's license and the third-party notices file

#### Scenario: A shipped native component has its notice
- **WHEN** the package contains a component's library, such as the transcription engine or its compute libraries
- **THEN** the third-party notices file contains that component's name and license text

#### Scenario: Installer code has its notice
- **WHEN** the package contains third-party code that runs while it is installed or uninstalled
- **THEN** the third-party notices file contains that component's name and license text

### Requirement: A release carries the source of its copyleft components
Each release SHALL carry, next to the installer, the source code of every third-party component in the installer package whose license requires its source to be available to those who receive its binaries, such as libuiohook under the GNU LGPL and the WiX Toolset's custom actions under the Microsoft Reciprocal License. Each such component SHALL have one archive, which SHALL hold the source of the exact commit that the third-party notices file names for that component, and whose file name SHALL contain that commit. The third-party notices file SHALL say, for each such component, that the release carries its source. If a source archive can't be obtained, no release SHALL be published.

#### Scenario: Source next to the installer
- **WHEN** the assets of the release for version `0.2.0` are listed
- **THEN** besides `Pisum.Transcribe_0.2.0_win-x64.msi`, they include one source archive for libuiohook and one for the WiX Toolset
- **AND** each archive's file name contains the commit that the third-party notices file names for that component

#### Scenario: A missing source archive publishes nothing
- **WHEN** a version tag is pushed and a source archive can't be downloaded
- **THEN** no release exists for that tag

### Requirement: One version per release
A release SHALL carry one version, taken from its tag with the leading `v` removed. The release name, the installer file name and the version the application writes to its log at start SHALL all show that version. Windows' list of installed apps SHALL show that version without a pre-release suffix, because a Windows Installer version has no such part. The repository SHALL record the version of the most recent release, and a build that is not a release SHALL report that recorded version.

#### Scenario: Versions agree
- **WHEN** the release for tag `v0.2.0` is published, and the application from its installer is installed and started
- **THEN** the release name and the installer file name contain `0.2.0`
- **AND** the log entry with the application version written at start shows `0.2.0`
- **AND** Windows' list of installed apps shows `0.2.0`

#### Scenario: Pre-release in the list of installed apps
- **WHEN** the installer of `0.2.0-rc.1` is installed and the application is started
- **THEN** the log entry with the application version written at start shows `0.2.0-rc.1`
- **AND** Windows' list of installed apps shows `0.2.0`

### Requirement: Publishing a release from a version tag
When a tag of the form `v<major>.<minor>.<patch>`, optionally followed by a pre-release suffix such as `-rc.1`, is pushed to the repository, a release for that tag SHALL be published on the project's GitHub Releases page with the installer attached. If any step before publishing fails, no release SHALL be published for that tag.

#### Scenario: Tag push publishes a release
- **WHEN** the tag `v0.2.0` is pushed
- **THEN** a release named for version `0.2.0` appears on the Releases page with `Pisum.Transcribe_0.2.0_win-x64.msi` attached

#### Scenario: A failed build publishes nothing
- **WHEN** a version tag is pushed and building the installer fails
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
Every release run SHALL build the solution and run the default test suite from the tagged commit before it builds the installer. If the build produces a warning or error, or any test fails, no release SHALL be published.

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
