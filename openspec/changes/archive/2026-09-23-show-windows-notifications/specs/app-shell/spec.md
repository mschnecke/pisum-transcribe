## ADDED Requirements

### Requirement: Notifications come from Pisum Transcribe
Every notification the application shows SHALL be a Windows notification with the sender name "Pisum Transcribe" and the application's icon, both when the application is installed and when it runs from a build that isn't installed. Windows SHALL list Pisum Transcribe in its notification settings, where the user can turn its notifications off. A notification SHALL stay in Windows' notification center until the user clears it, also after the application has ended. A notification SHALL have no click action. When Windows doesn't show a notification, for example because the user turned them off, the application SHALL keep running.

#### Scenario: A notification names the application
- **WHEN** the application shows a notification
- **THEN** Windows shows it with the sender name "Pisum Transcribe" and the application's icon

#### Scenario: Windows lists the application in its notification settings
- **WHEN** the application has shown a notification
- **THEN** Windows' notification settings list "Pisum Transcribe", and the user can turn its notifications off there

#### Scenario: A notification outlives the application
- **WHEN** the application shows a notification and then ends
- **THEN** the notification is still in Windows' notification center until the user clears it

#### Scenario: Notifications turned off
- **WHEN** the user turned off notifications for Pisum Transcribe and the application shows a notification
- **THEN** no notification appears, and the application keeps running

## MODIFIED Requirements

### Requirement: Unhandled errors end the application visibly
Unhandled exceptions SHALL be written to the log. When an unhandled exception occurs on the UI thread, or a background service stops because of an unhandled exception, the application SHALL notify the user with a Windows notification that Pisum Transcribe stopped because of an error, remove the tray icon, and end the process within 5 seconds with a non-zero exit code. The notification SHALL stay in Windows' notification center after the process has ended. An exception in a background task that nothing observes SHALL be logged, and the application SHALL keep running. An exception that ends the process immediately SHALL be written to the log before the process ends.

#### Scenario: Error on the UI thread
- **WHEN** an exception on the UI thread is not handled
- **THEN** the exception details are written to the log file
- **AND** a Windows notification tells the user that Pisum Transcribe stopped because of an error
- **AND** the process ends within 5 seconds with a non-zero exit code
- **AND** the notification is still in Windows' notification center after the process has ended

#### Scenario: Background service fails
- **WHEN** a background service stops because of an unhandled exception
- **THEN** the exception details are written to the log file
- **AND** a Windows notification tells the user that Pisum Transcribe stopped because of an error
- **AND** the process ends within 5 seconds with a non-zero exit code
- **AND** the notification is still in Windows' notification center after the process has ended

#### Scenario: Unobserved background task fails
- **WHEN** a background task throws an exception that nothing observes
- **THEN** the exception details are written to the log file
- **AND** the application keeps running

#### Scenario: Fatal error
- **WHEN** an exception ends the process immediately
- **THEN** the exception details are written to the log file before the process ends
