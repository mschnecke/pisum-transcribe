## ADDED Requirements

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
