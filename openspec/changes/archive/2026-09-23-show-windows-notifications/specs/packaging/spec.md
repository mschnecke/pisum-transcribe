## MODIFIED Requirements

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
