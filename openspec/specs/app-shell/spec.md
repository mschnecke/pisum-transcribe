# app-shell Specification

## Purpose

Defines how Pisum Transcribe starts, runs in the background from the system tray, prevents duplicate instances, stores its local data and logs, and shuts down.

## Requirements

### Requirement: Tray-resident startup
The application SHALL start without showing a main window and SHALL show a status icon for as long as it runs: in the notification area (tray) on Windows, and in the menu bar on macOS. The icon's tooltip SHALL contain the product name "Pisum Transcribe". On macOS the application SHALL show no Dock icon and SHALL NOT appear in the app switcher. The menu bar icon SHALL follow the menu bar's appearance in light and dark mode while the application is ready or unavailable.

#### Scenario: Application starts
- **WHEN** the user launches Pisum Transcribe on Windows
- **THEN** a tray icon with the tooltip "Pisum Transcribe" appears
- **AND** no main window and no taskbar button are shown

#### Scenario: Application starts on macOS
- **WHEN** the user launches Pisum Transcribe on macOS
- **THEN** a menu bar icon with the tooltip "Pisum Transcribe" appears
- **AND** no main window and no Dock icon are shown
- **AND** Pisum Transcribe doesn't appear in the app switcher

#### Scenario: Menu bar appearance changes on macOS
- **WHEN** the application is ready on macOS and the user switches between light and dark mode
- **THEN** the menu bar icon is drawn in the menu bar's color for that mode

### Requirement: Exit from tray
The tray icon's menu SHALL end with an item that ends the application: **Exit** on Windows, and **Quit Pisum Transcribe** on macOS. Choosing it SHALL stop all background work, remove the tray icon and end the process within 5 seconds. If background work has not stopped by then, the application SHALL end the process anyway.

#### Scenario: User exits the application
- **WHEN** the user right-clicks the tray icon on Windows and chooses **Exit**
- **THEN** the tray icon disappears
- **AND** the process ends within 5 seconds

#### Scenario: User quits the application on macOS
- **WHEN** the user clicks the menu bar icon on macOS and chooses **Quit Pisum Transcribe**
- **THEN** the menu bar icon disappears
- **AND** the process ends within 5 seconds

#### Scenario: Background work does not stop in time
- **WHEN** the user chooses **Exit** or **Quit Pisum Transcribe** and a background task does not stop
- **THEN** the process still ends within 5 seconds of the choice

### Requirement: Single instance per user session
Only one instance of the application SHALL run per Windows user session on Windows, and per user on macOS, whether it was launched from Finder, from the terminal or at login. A second launch SHALL end without creating a second tray icon. A launch while the previous instance is still exiting SHALL start once that instance has ended.

#### Scenario: Second launch while running
- **WHEN** Pisum Transcribe is already running and the user launches it again
- **THEN** the second process ends
- **AND** exactly one tray icon remains

#### Scenario: Second launch in another way on macOS
- **WHEN** Pisum Transcribe was started from the terminal on macOS and the user launches it again from Finder, or it was started from Finder and the user starts it again from the terminal
- **THEN** the second process ends
- **AND** exactly one menu bar icon remains

#### Scenario: Launch after previous instance exited
- **WHEN** the previous instance has exited, including by a crash, and the user launches the application
- **THEN** the application starts normally

#### Scenario: Launch while previous instance is exiting
- **WHEN** the user chooses **Exit** or **Quit Pisum Transcribe** and launches Pisum Transcribe again before the previous process has ended
- **THEN** the new instance starts after the previous process has ended
- **AND** exactly one tray icon remains

### Requirement: Local data folder
The application SHALL keep all of its per-user data under `%LOCALAPPDATA%\Pisum Transcribe\` on Windows and under `~/Library/Application Support/Pisum Transcribe/` on macOS, except the logs on macOS (see "Local rolling log files"), and SHALL create this folder on first start if it does not exist.

#### Scenario: First start on a new machine
- **WHEN** the application starts on Windows and `%LOCALAPPDATA%\Pisum Transcribe\` does not exist
- **THEN** the folder is created

#### Scenario: First start on a new Mac
- **WHEN** the application starts on macOS and `~/Library/Application Support/Pisum Transcribe/` does not exist
- **THEN** the folder is created

### Requirement: Local rolling log files
The application SHALL write diagnostic logs to `%LOCALAPPDATA%\Pisum Transcribe\logs\` on Windows and to `~/Library/Logs/Pisum Transcribe/` on macOS, with one file per day, and SHALL keep at most the 7 most recent files. It SHALL create the log folder if it does not exist. The application SHALL NOT send logs or usage data over the network.

#### Scenario: Startup is logged
- **WHEN** the application starts
- **THEN** a log entry with the application version is written to the current day's log file

#### Scenario: Logs on macOS
- **WHEN** the application starts on macOS and `~/Library/Logs/Pisum Transcribe/` does not exist
- **THEN** the folder is created and holds the current day's log file

#### Scenario: Old logs are pruned
- **WHEN** more than 7 daily log files exist
- **THEN** the oldest files are deleted, leaving 7

### Requirement: Unhandled errors end the application visibly
Unhandled exceptions SHALL be written to the log. When an unhandled exception occurs on the UI thread, or a background service stops because of an unhandled exception, the application SHALL notify the user with a notification (see "Notifications come from Pisum Transcribe") that Pisum Transcribe stopped because of an error, remove the tray icon, and end the process within 5 seconds with a non-zero exit code. The notification SHALL stay in the notification center, Windows' or macOS's, after the process has ended. An exception in a background task that nothing observes SHALL be logged, and the application SHALL keep running. An exception that ends the process immediately SHALL be written to the log before the process ends.

#### Scenario: Error on the UI thread
- **WHEN** an exception on the UI thread is not handled
- **THEN** the exception details are written to the log file
- **AND** a notification tells the user that Pisum Transcribe stopped because of an error
- **AND** the process ends within 5 seconds with a non-zero exit code
- **AND** the notification is still in the notification center after the process has ended

#### Scenario: Background service fails
- **WHEN** a background service stops because of an unhandled exception
- **THEN** the exception details are written to the log file
- **AND** a notification tells the user that Pisum Transcribe stopped because of an error
- **AND** the process ends within 5 seconds with a non-zero exit code
- **AND** the notification is still in the notification center after the process has ended

#### Scenario: Unobserved background task fails
- **WHEN** a background task throws an exception that nothing observes
- **THEN** the exception details are written to the log file
- **AND** the application keeps running

#### Scenario: Fatal error
- **WHEN** an exception ends the process immediately
- **THEN** the exception details are written to the log file before the process ends

### Requirement: End with the Windows session
When the Windows session ends while the application runs, because the user signs out, shuts Windows down or restarts it, the application SHALL end as it does when the user chooses **Exit**: it SHALL stop all background work, remove the tray icon and end the process within 5 seconds. If background work has not stopped by then, the application SHALL end the process anyway. The application SHALL NOT prevent the session from ending, and SHALL write to the log that it ended because the Windows session ended.

#### Scenario: User signs out, shuts down or restarts
- **WHEN** Pisum Transcribe runs and the user signs out of Windows, shuts it down or restarts it
- **THEN** the log shows that Pisum Transcribe ended because the Windows session ended
- **AND** the process ends within 5 seconds
- **AND** Windows does not show Pisum Transcribe as an app that prevents the session from ending

#### Scenario: Background work does not stop in time
- **WHEN** the Windows session ends and a background task does not stop
- **THEN** the process still ends within 5 seconds
- **AND** Windows does not show Pisum Transcribe as an app that prevents the session from ending

### Requirement: End with the macOS session
When the macOS session ends while the application runs, because the user logs out, shuts the Mac down or restarts it, the application SHALL end as it does when the user chooses **Quit Pisum Transcribe**: it SHALL stop all background work, remove the menu bar icon and end the process within 5 seconds. If background work has not stopped by then, the application SHALL end the process anyway. The application SHALL NOT prevent or delay the end of the session, and SHALL write to the log that it ended because the macOS session ended.

#### Scenario: User logs out, shuts down or restarts on macOS
- **WHEN** Pisum Transcribe runs on macOS and the user logs out, shuts the Mac down or restarts it
- **THEN** the log shows that Pisum Transcribe ended because the macOS session ended
- **AND** the process ends within 5 seconds
- **AND** macOS doesn't report that Pisum Transcribe interrupted the logout, shutdown or restart

#### Scenario: Background work does not stop in time on macOS
- **WHEN** the macOS session ends and a background task does not stop
- **THEN** the process still ends within 5 seconds
- **AND** macOS doesn't report that Pisum Transcribe interrupted the logout, shutdown or restart

### Requirement: Termination request on macOS
When the application receives a termination request (`SIGTERM`) on macOS, for example from an installer or from `kill`, it SHALL end as it does when the user chooses **Quit Pisum Transcribe**: it SHALL stop all background work, remove the menu bar icon and end the process within 5 seconds, and SHALL write to the log that it ended because of the termination request.

#### Scenario: Termination request while running on macOS
- **WHEN** Pisum Transcribe runs on macOS and receives `SIGTERM`
- **THEN** the log shows that Pisum Transcribe ended because of a termination request
- **AND** the menu bar icon disappears
- **AND** the process ends within 5 seconds

### Requirement: Notifications come from Pisum Transcribe
Every notification the application shows SHALL be a system notification with the sender name "Pisum Transcribe" and the application's icon: a Windows notification on Windows, and a macOS notification on macOS. On Windows this SHALL hold both when the application is installed and when it runs from a build that isn't installed. On macOS it SHALL hold when the application runs as an app bundle; a build that doesn't run as an app bundle SHALL write the notification to the log instead. The system SHALL list Pisum Transcribe in its notification settings, where the user can turn its notifications off. On macOS the application SHALL ask for permission to show notifications when the setup window opens and the user hasn't answered yet, and SHALL NOT ask at a start that doesn't open the setup window (see `macos-permissions`). A notification SHALL stay in the notification center until the user clears it, also after the application has ended. A notification SHALL have no click action. When the system doesn't show a notification, for example because the user turned them off or refused the permission, the application SHALL keep running.

#### Scenario: A notification names the application
- **WHEN** the application shows a notification
- **THEN** the system shows it with the sender name "Pisum Transcribe" and the application's icon

#### Scenario: Windows lists the application in its notification settings
- **WHEN** the application has shown a notification on Windows
- **THEN** Windows' notification settings list "Pisum Transcribe", and the user can turn its notifications off there

#### Scenario: macOS lists the application in its notification settings
- **WHEN** the application has shown a notification on macOS
- **THEN** System Settings → Notifications lists "Pisum Transcribe", and the user can turn its notifications off there

#### Scenario: Permission asked on the first start on macOS
- **WHEN** Pisum Transcribe starts on macOS for the first time and opens the setup window
- **THEN** macOS asks the user whether Pisum Transcribe may show notifications
- **AND** it doesn't ask again at later starts

#### Scenario: A notification outlives the application
- **WHEN** the application shows a notification and then ends
- **THEN** the notification is still in the notification center until the user clears it

#### Scenario: Notifications turned off
- **WHEN** the user turned off notifications for Pisum Transcribe, or refused the permission on macOS, and the application shows a notification
- **THEN** no notification appears, and the application keeps running

#### Scenario: Not running as an app bundle on macOS
- **WHEN** the application runs on macOS without an app bundle and shows a notification
- **THEN** the notification's title and message are written to the log, and the application keeps running
