## Purpose

Defines how Pisum Transcribe starts, runs in the background from the system tray, prevents duplicate instances, stores its local data and logs, and shuts down.

## ADDED Requirements

### Requirement: Tray-resident startup
The application SHALL start without showing a main window and SHALL show a notification-area (tray) icon for as long as it runs. The icon's tooltip SHALL contain the product name "Pisum Transcribe".

#### Scenario: Application starts
- **WHEN** the user launches Pisum Transcribe
- **THEN** a tray icon with the tooltip "Pisum Transcribe" appears
- **AND** no main window and no taskbar button are shown

### Requirement: Exit from tray
The tray icon SHALL provide a context menu with an **Exit** item. Choosing Exit SHALL stop all background work, remove the tray icon and end the process within 5 seconds. If background work has not stopped by then, the application SHALL end the process anyway.

#### Scenario: User exits the application
- **WHEN** the user right-clicks the tray icon and chooses **Exit**
- **THEN** the tray icon disappears
- **AND** the process ends within 5 seconds

#### Scenario: Background work does not stop in time
- **WHEN** the user chooses **Exit** and a background task does not stop
- **THEN** the process still ends within 5 seconds of choosing **Exit**

### Requirement: Single instance per user session
Only one instance of the application SHALL run per Windows user session. A second launch SHALL end without creating a second tray icon. A launch while the previous instance is still exiting SHALL start once that instance has ended.

#### Scenario: Second launch while running
- **WHEN** Pisum Transcribe is already running and the user launches it again
- **THEN** the second process ends
- **AND** exactly one tray icon remains

#### Scenario: Launch after previous instance exited
- **WHEN** the previous instance has exited, including by a crash, and the user launches the application
- **THEN** the application starts normally

#### Scenario: Launch while previous instance is exiting
- **WHEN** the user chooses **Exit** and launches Pisum Transcribe again before the previous process has ended
- **THEN** the new instance starts after the previous process has ended
- **AND** exactly one tray icon remains

### Requirement: Local data folder
The application SHALL keep all of its per-user data under `%LOCALAPPDATA%\Pisum Transcribe\` and SHALL create this folder on first start if it does not exist.

#### Scenario: First start on a new machine
- **WHEN** the application starts and `%LOCALAPPDATA%\Pisum Transcribe\` does not exist
- **THEN** the folder is created

### Requirement: Local rolling log files
The application SHALL write diagnostic logs to `%LOCALAPPDATA%\Pisum Transcribe\logs\`, with one file per day, and SHALL keep at most the 7 most recent files. The application SHALL NOT send logs or usage data over the network.

#### Scenario: Startup is logged
- **WHEN** the application starts
- **THEN** a log entry with the application version is written to the current day's log file

#### Scenario: Old logs are pruned
- **WHEN** more than 7 daily log files exist
- **THEN** the oldest files are deleted, leaving 7

### Requirement: Unhandled errors end the application visibly
Unhandled exceptions SHALL be written to the log. When an unhandled exception occurs on the UI thread, or a background service stops because of an unhandled exception, the application SHALL notify the user that Pisum Transcribe stopped because of an error, remove the tray icon, and end the process within 5 seconds with a non-zero exit code. An exception in a background task that nothing observes SHALL be logged, and the application SHALL keep running. An exception that ends the process immediately SHALL be written to the log before the process ends.

#### Scenario: Error on the UI thread
- **WHEN** an exception on the UI thread is not handled
- **THEN** the exception details are written to the log file
- **AND** a tray notification tells the user that Pisum Transcribe stopped because of an error
- **AND** the process ends within 5 seconds with a non-zero exit code

#### Scenario: Background service fails
- **WHEN** a background service stops because of an unhandled exception
- **THEN** the exception details are written to the log file
- **AND** a tray notification tells the user that Pisum Transcribe stopped because of an error
- **AND** the process ends within 5 seconds with a non-zero exit code

#### Scenario: Unobserved background task fails
- **WHEN** a background task throws an exception that nothing observes
- **THEN** the exception details are written to the log file
- **AND** the application keeps running

#### Scenario: Fatal error
- **WHEN** an exception ends the process immediately
- **THEN** the exception details are written to the log file before the process ends
