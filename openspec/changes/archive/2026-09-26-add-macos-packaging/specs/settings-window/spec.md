## MODIFIED Requirements

### Requirement: Start with Windows
On Windows, the general section SHALL offer "Start with Windows", which is off until the user turns it on. When saved as on, Pisum Transcribe SHALL start automatically when the user signs in to Windows, also when its startup entry was disabled in Task Manager before. When saved as off, it SHALL no longer start automatically. The option SHALL show whether Windows will start Pisum Transcribe at sign-in, including after a change in Task Manager, and SHALL NOT be stored in the settings file. The option SHALL affect only the current Windows user and SHALL NOT require administrator rights.

On macOS, the general section SHALL offer "Open at login" instead, which is off until the user turns it on. When saved as on, Pisum Transcribe SHALL open when the user logs in to macOS. When saved as off, it SHALL no longer open at login. The option SHALL show whether macOS will open Pisum Transcribe at login, including after a change in *System Settings → General → Login Items*, and SHALL NOT be stored in the settings file. When macOS needs the user's approval before it opens Pisum Transcribe at login, for example because the user turned it off in Login Items, the option SHALL be shown as off, and a hint SHALL say to allow Pisum Transcribe in *System Settings → General → Login Items*. The option SHALL affect only the current macOS user and SHALL NOT require an administrator's password.

#### Scenario: Enable autostart
- **WHEN** the user enables "Start with Windows", saves, and signs out and in again
- **THEN** Pisum Transcribe is running with its tray icon

#### Scenario: Disable autostart
- **WHEN** the user disables "Start with Windows", saves, and signs out and in again
- **THEN** Pisum Transcribe is not started automatically

#### Scenario: Disabled in Task Manager
- **WHEN** "Start with Windows" is on and the user disables Pisum Transcribe on Task Manager's startup apps page
- **THEN** the next time the settings window opens, "Start with Windows" is off

#### Scenario: Enabled again after Task Manager
- **WHEN** Pisum Transcribe was disabled in Task Manager, and the user enables "Start with Windows", saves, and signs out and in again
- **THEN** Pisum Transcribe is running with its tray icon

#### Scenario: Enable open at login on macOS
- **WHEN** the user enables "Open at login" on macOS, saves, and logs out and in again
- **THEN** Pisum Transcribe is running with its menu bar icon
- **AND** *System Settings → General → Login Items* lists Pisum Transcribe

#### Scenario: Disable open at login on macOS
- **WHEN** the user disables "Open at login" on macOS, saves, and logs out and in again
- **THEN** Pisum Transcribe isn't opened automatically

#### Scenario: Turned off in Login Items on macOS
- **WHEN** "Open at login" is on and the user turns Pisum Transcribe off in *System Settings → General → Login Items*
- **THEN** the next time the settings window opens, "Open at login" is off, and the hint says to allow Pisum Transcribe in Login Items

#### Scenario: No autostart option on macOS
- **WHEN** the user opens the settings window on macOS
- **THEN** the general section shows "Open at login" and doesn't show "Start with Windows"
