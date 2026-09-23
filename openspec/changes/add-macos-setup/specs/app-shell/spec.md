## MODIFIED Requirements

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
