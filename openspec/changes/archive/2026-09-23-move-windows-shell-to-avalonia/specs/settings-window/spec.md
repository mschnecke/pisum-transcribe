## MODIFIED Requirements

### Requirement: Opening the settings window
The tray menu SHALL contain a **Settings…** item, and a left click on the tray icon SHALL also open the settings window. A right click on the tray icon SHALL open the tray menu and SHALL NOT open the settings window. If the window is already open, it SHALL be brought to the foreground instead of opening a second one.

#### Scenario: Open from tray menu
- **WHEN** the user chooses **Settings…** in the tray menu
- **THEN** the settings window opens showing the currently saved settings

#### Scenario: Open with a click on the tray icon
- **WHEN** the settings window is closed and the user left-clicks the tray icon
- **THEN** the settings window opens showing the currently saved settings

#### Scenario: Right click opens the menu
- **WHEN** the user right-clicks the tray icon
- **THEN** the tray menu opens
- **AND** the settings window doesn't open

#### Scenario: Already open
- **WHEN** the settings window is open and the user left-clicks the tray icon
- **THEN** the existing window is activated
- **AND** no second settings window opens

#### Scenario: Double-click on the tray icon
- **WHEN** the settings window is closed and the user double-clicks the tray icon
- **THEN** one settings window is open and in the foreground
