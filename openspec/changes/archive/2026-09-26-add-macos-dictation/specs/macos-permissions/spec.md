## MODIFIED Requirements

### Requirement: Relaunch after the Accessibility grant
On macOS, when Accessibility turns from not granted to granted while the application runs, the application SHALL restart itself, so that the push-to-talk hotkey can use the grant, which macOS makes visible only to a new process. This SHALL hold whether the setup window is open or closed, and also when the grant was in place at start and was revoked while the application runs: from the revoke on, the grant SHALL count as not in effect until the restart, so the setup window SHALL NOT close as complete on a new grant alone. While a model download is running, the restart SHALL wait until the download has succeeded, failed or been cancelled. While a dictation is in progress, the restart SHALL wait until the dictation has ended, including its transcription and its insertion. Before the restart, the setup window, when it is open, SHALL say for at least 3 seconds that Pisum Transcribe restarts. The restart SHALL end the application as **Quit Pisum Transcribe** does, and the new instance SHALL start once the old one has ended. After the restart, the setup window SHALL open again when the selected model isn't installed or a required permission is missing.

#### Scenario: Grant without a download
- **WHEN** no download is running and the user grants Accessibility
- **THEN** the setup window says that Pisum Transcribe restarts
- **AND** after at least 3 seconds the application ends and starts again, with exactly one menu bar icon

#### Scenario: Grant during a download
- **WHEN** a model download is running and the user grants Accessibility
- **THEN** the application doesn't restart while the download runs
- **AND** the Accessibility row says that Pisum Transcribe restarts when the download is finished
- **AND** after the download succeeds, the application restarts

#### Scenario: Grant again during a transcription
- **WHEN** the user revokes Accessibility while a long dictation is being transcribed, and grants it again before the transcription has ended
- **THEN** the application doesn't restart until the dictation has ended
- **AND** the transcript is left on the clipboard with a notification
- **AND** the application then restarts

#### Scenario: Setup still incomplete after the restart
- **WHEN** the application restarts after the Accessibility grant and the microphone permission is still missing
- **THEN** the setup window opens again after the restart

#### Scenario: Grant while the window is closed
- **WHEN** the setup window is closed and the user grants Accessibility in System Settings
- **THEN** the application restarts within 10 seconds

#### Scenario: Grant again after a revoke
- **WHEN** the application started with the Accessibility grant, the user revoked it while the application runs, and the user then grants it again through **Set up Pisum Transcribe…**
- **THEN** the setup window says that Pisum Transcribe restarts, and doesn't close as complete before that
- **AND** after the restart, holding the push-to-talk hotkey raises *pressed*
