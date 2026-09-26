## ADDED Requirements

### Requirement: macOS installer for Apple silicon
Each release SHALL provide one installer package for macOS on Apple silicon, named `Pisum.Transcribe_<version>_osx-arm64.pkg`. It SHALL install `Pisum Transcribe.app` into `/Applications`, and SHALL NOT install into or update a copy of the application in any other folder, even one with the same bundle identifier. The package SHALL contain the application and every runtime dependency it needs, so that the installed application starts, detects speech and transcribes on a Mac with Apple silicon and macOS 14 or later that has no .NET runtime installed, without installing anything else first. It SHALL refuse to install on a Mac with an Intel processor or with macOS 13 or older, with a message. The package itself isn't signed: a downloaded package SHALL install after the user allows it once with **Open Anyway** in *System Settings → Privacy & Security*, and the installed application SHALL then open without a further Gatekeeper prompt. When the package is installed interactively, by opening it, the installer SHALL start the application for the user who installs it when it finishes. An installation from the command line, such as `sudo installer -pkg <package> -target /`, SHALL NOT start it.

#### Scenario: Install from a download
- **WHEN** a user downloads the package of a release, opens it, allows it with **Open Anyway**, and finishes the installation with an administrator's password
- **THEN** `/Applications/Pisum Transcribe.app` exists, and the application runs with its menu bar icon
- **AND** without a downloaded speech model, it opens the setup window

#### Scenario: Another copy with the same identifier stays untouched
- **WHEN** a development build of the application with the same bundle identifier exists outside `/Applications`, and the package is installed
- **THEN** the package installs into `/Applications`, and the development build is unchanged

#### Scenario: A command-line installation doesn't start the application
- **WHEN** the package is installed with `sudo installer -pkg <package> -target /`
- **THEN** the installation completes and the application isn't running

#### Scenario: Refused on an unsupported Mac
- **WHEN** the package is opened on macOS 13, or on a Mac with an Intel processor
- **THEN** the installer shows a message and installs nothing

#### Scenario: File name carries version and platform
- **WHEN** the assets of the release for version `1.4.0` are listed
- **THEN** the macOS installer is named `Pisum.Transcribe_1.4.0_osx-arm64.pkg`

### Requirement: Release signing on macOS
The application in the macOS installer SHALL be signed with the project's own code-signing certificate, the same one for every release, so that macOS keeps the user's Accessibility and microphone grants when a newer release is installed over an older one. No release SHALL be published if the application isn't signed with that certificate, if a library in it is built for another architecture than Apple silicon or for a macOS newer than 14, or if the application contains a native Windows or Linux file.

#### Scenario: Grants survive an update
- **WHEN** a user granted Accessibility and the microphone to an installed release, and installs a newer release over it
- **THEN** the newer version records and inserts text without asking for either permission again

#### Scenario: Wrong certificate stops the release
- **WHEN** the release run signs the application with a certificate other than the project's
- **THEN** no release is published for that tag

## MODIFIED Requirements

### Requirement: Upgrading in place
Installing the package of a release while another version is installed SHALL replace the installed application in its folder, so that exactly one copy stays installed. A package whose version differs from the installed one only by a pre-release suffix SHALL also replace it, so that a final release installs over its release candidates. A package whose version, without its pre-release suffix, is lower than the installed one SHALL be refused with a message, and the installed application SHALL stay unchanged. An upgrade SHALL keep the user's settings, logs, downloaded speech models and "Start with Windows" entry, and on macOS its "Open at login" item. If the application is running, it SHALL end as it does when the user chooses **Exit** before its files are replaced, and the upgrade SHALL NOT need a restart of Windows. After an interactive upgrade, the installer SHALL start the new version, as after an interactive installation.

On macOS, the same SHALL hold for the package in `/Applications`: an older package SHALL be refused with a message that a newer version is installed, and every running instance of the application, also one of another user logged in at the same time, SHALL end as it does at **Quit Pisum Transcribe** before its files are replaced. An instance that hasn't ended after 6 seconds SHALL be ended. After an interactive upgrade, the new version SHALL start for the user who installs it.

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

#### Scenario: Upgrade while the application runs on macOS
- **WHEN** the macOS package of `1.4.0` is opened while `1.4.0-rc.1` runs from `/Applications`
- **THEN** the running application ends as it does at **Quit Pisum Transcribe**, and its log shows the shutdown
- **AND** `1.4.0` starts when the installation finishes, with the settings, the downloaded models, the "Open at login" setting and the permission grants as before

#### Scenario: Older release refused on macOS
- **WHEN** the macOS package of `1.4.0-rc.1` is opened while `1.4.0` is installed
- **THEN** the installer shows a message that a newer version is installed, and `1.4.0` stays installed

### Requirement: Uninstalling
Uninstalling the application through Windows' list of installed apps SHALL remove the application's program folder, its Start Menu shortcut, its "Start with Windows" entry, including an entry the user disabled in Task Manager, and its notification registration, the per-user registry key `HKCU\Software\Classes\AppUserModelId\Pisum.Transcribe`. It SHALL keep the per-user data folder `%LOCALAPPDATA%\Pisum Transcribe\` with the settings, the logs and the downloaded speech models. An upgrade SHALL NOT remove the "Start with Windows" entry or the notification registration. If the application is running, it SHALL end as it does when the user chooses **Exit** before its files are removed.

On macOS, the application SHALL be uninstalled by moving `/Applications/Pisum Transcribe.app` to the Trash, which SHALL also end its "Open at login" item. The settings and downloaded speech models in `~/Library/Application Support/Pisum Transcribe/` and the logs in `~/Library/Logs/Pisum Transcribe/` SHALL stay. The project's README SHALL describe this, and how to also remove those folders, the installer's receipt and the permission grants.

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

#### Scenario: Moving the app to the Trash on macOS
- **WHEN** "Open at login" is on, the user quits the application, moves `/Applications/Pisum Transcribe.app` to the Trash, and logs out and in again
- **THEN** Pisum Transcribe isn't started, and *System Settings → General → Login Items* doesn't list it
- **AND** `~/Library/Application Support/Pisum Transcribe/` still holds the settings and the downloaded models

### Requirement: The installed application ships the license notices
The application folder that the Windows installer creates, and the `Contents/Resources` folder of the application bundle that the macOS installer installs, SHALL contain the project's license and a third-party notices file. The notices file SHALL name every third-party component whose code the installer package contains, including code that runs only while the package is installed or uninstalled, and SHALL include the license text or notice that the component's license requires. Speech models are not in the package and are not covered by this requirement.

#### Scenario: Notices are next to the application
- **WHEN** the package is installed
- **THEN** `%LOCALAPPDATA%\Programs\Pisum Transcribe\` contains the project's license and the third-party notices file

#### Scenario: Notices are in the macOS bundle
- **WHEN** the macOS package is installed
- **THEN** `/Applications/Pisum Transcribe.app/Contents/Resources/` contains the project's license and the third-party notices file

#### Scenario: A shipped native component has its notice
- **WHEN** the package contains a component's library, such as the transcription engine or its compute libraries
- **THEN** the third-party notices file contains that component's name and license text

#### Scenario: Installer code has its notice
- **WHEN** the package contains third-party code that runs while it is installed or uninstalled
- **THEN** the third-party notices file contains that component's name and license text

### Requirement: A release carries the source of its copyleft components
Each release SHALL carry, next to the installers, the source code of every third-party component in an installer package whose license requires its source to be available to those who receive its binaries, such as libuiohook under the GNU LGPL, which both installers contain, and the WiX Toolset's custom actions under the Microsoft Reciprocal License. Each such component SHALL have one archive, which SHALL hold the source of the exact commit that the third-party notices file names for that component, and whose file name SHALL contain that commit. The third-party notices file SHALL say, for each such component, that the release carries its source. If a source archive can't be obtained, no release SHALL be published.

#### Scenario: Source next to the installer
- **WHEN** the assets of the release for version `0.2.0` are listed
- **THEN** besides `Pisum.Transcribe_0.2.0_win-x64.msi`, they include one source archive for libuiohook and one for the WiX Toolset
- **AND** each archive's file name contains the commit that the third-party notices file names for that component

#### Scenario: Source next to both installers
- **WHEN** the assets of the release for version `1.4.0` are listed
- **THEN** besides `Pisum.Transcribe_1.4.0_win-x64.msi` and `Pisum.Transcribe_1.4.0_osx-arm64.pkg`, they include one source archive for libuiohook and one for the WiX Toolset

#### Scenario: A missing source archive publishes nothing
- **WHEN** a version tag is pushed and a source archive can't be downloaded
- **THEN** no release exists for that tag

### Requirement: One version per release
A release SHALL carry one version, taken from its tag with the leading `v` removed. The release name, the installer file names and the version the application writes to its log at start SHALL all show that version. Windows' list of installed apps SHALL show that version without a pre-release suffix, because a Windows Installer version has no such part. On macOS, the installed application bundle SHALL carry the whole version, with a pre-release suffix. The repository SHALL record the version of the most recent release, and a build that is not a release SHALL report that recorded version.

#### Scenario: Versions agree
- **WHEN** the release for tag `v0.2.0` is published, and the application from its installer is installed and started
- **THEN** the release name and the installer file name contain `0.2.0`
- **AND** the log entry with the application version written at start shows `0.2.0`
- **AND** Windows' list of installed apps shows `0.2.0`

#### Scenario: Pre-release in the list of installed apps
- **WHEN** the installer of `0.2.0-rc.1` is installed and the application is started
- **THEN** the log entry with the application version written at start shows `0.2.0-rc.1`
- **AND** Windows' list of installed apps shows `0.2.0`

#### Scenario: Pre-release on macOS
- **WHEN** the macOS installer of `1.4.0-rc.1` is installed and the application is started
- **THEN** the log entry with the application version written at start shows `1.4.0-rc.1`
- **AND** the version of `/Applications/Pisum Transcribe.app` is `1.4.0-rc.1`

### Requirement: Publishing a release from a version tag
When a tag of the form `v<major>.<minor>.<patch>`, optionally followed by a pre-release suffix such as `-rc.1`, is pushed to the repository, a release for that tag SHALL be published on the project's GitHub Releases page with the Windows installer and the macOS installer attached. If any step before publishing fails on either platform, no release SHALL be published for that tag, so that a release never carries only one of the installers.

#### Scenario: Tag push publishes a release
- **WHEN** the tag `v0.2.0` is pushed
- **THEN** a release named for version `0.2.0` appears on the Releases page with `Pisum.Transcribe_0.2.0_win-x64.msi` and `Pisum.Transcribe_0.2.0_osx-arm64.pkg` attached

#### Scenario: A failed build publishes nothing
- **WHEN** a version tag is pushed and building the installer fails
- **THEN** no release exists for that tag

#### Scenario: A failed macOS build publishes nothing
- **WHEN** a version tag is pushed, the Windows installer builds, and building the macOS installer fails
- **THEN** no release exists for that tag, and the MSI isn't published either

### Requirement: Only a tested build is released
Every release run SHALL build the solution and run the default test suite from the tagged commit, on Windows and on macOS, before it builds the installer for that platform. If the build produces a warning or error, or any test fails, on either platform, no release SHALL be published.

#### Scenario: Failing test blocks the release
- **WHEN** a version tag is pushed on a commit where a test in the default test run fails
- **THEN** no release exists for that tag, and the run shows the failing test

#### Scenario: Failing macOS test blocks the release
- **WHEN** a version tag is pushed on a commit where a test fails only on macOS
- **THEN** no release exists for that tag, and the run shows the failing macOS test
