## MODIFIED Requirements

### Requirement: Opening the settings window
The tray menu SHALL contain a **Settings…** item. On Windows, a left click on the tray icon SHALL also open the settings window, and a right click on the tray icon SHALL open the tray menu and SHALL NOT open the settings window. On macOS, a click on the menu bar icon SHALL open the menu and SHALL NOT open the settings window, so **Settings…** is the only way to open it. If the window is already open, it SHALL be brought to the foreground instead of opening a second one.

#### Scenario: Open from tray menu
- **WHEN** the user chooses **Settings…** in the tray menu
- **THEN** the settings window opens showing the currently saved settings

#### Scenario: Open with a click on the tray icon
- **WHEN** the settings window is closed and the user left-clicks the tray icon on Windows
- **THEN** the settings window opens showing the currently saved settings

#### Scenario: Right click opens the menu
- **WHEN** the user right-clicks the tray icon on Windows
- **THEN** the tray menu opens
- **AND** the settings window doesn't open

#### Scenario: Click on the menu bar icon on macOS
- **WHEN** the user clicks the menu bar icon on macOS
- **THEN** the menu opens
- **AND** the settings window doesn't open

#### Scenario: Already open
- **WHEN** the settings window is open and the user left-clicks the tray icon on Windows, or chooses **Settings…**
- **THEN** the existing window is activated
- **AND** no second settings window opens

#### Scenario: Double-click on the tray icon
- **WHEN** the settings window is closed and the user double-clicks the tray icon on Windows
- **THEN** one settings window is open and in the foreground

### Requirement: Start with Windows
On Windows, the general section SHALL offer "Start with Windows", which is off until the user turns it on. When saved as on, Pisum Transcribe SHALL start automatically when the user signs in to Windows, also when its startup entry was disabled in Task Manager before. When saved as off, it SHALL no longer start automatically. The option SHALL show whether Windows will start Pisum Transcribe at sign-in, including after a change in Task Manager, and SHALL NOT be stored in the settings file. The option SHALL affect only the current Windows user and SHALL NOT require administrator rights. On macOS, the general section SHALL NOT show the option.

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

#### Scenario: No autostart option on macOS
- **WHEN** the user opens the settings window on macOS
- **THEN** the general section doesn't show "Start with Windows"
