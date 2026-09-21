## ADDED Requirements

### Requirement: Dictation ends when the application exits
When the application exits while a dictation is in progress, the application SHALL end the dictation at once, as a cancelled dictation does, and before the process ends: a running recording SHALL be aborted, its audio discarded and the microphone released, the overlay SHALL hide, and nothing SHALL be inserted. This SHALL apply in every phase of the dictation, from the overlay's starting look to the transcription.

#### Scenario: Exit during a recording
- **WHEN** a recording is running and the user chooses **Exit** from the tray while still holding the hotkey
- **THEN** the recording is aborted
- **AND** the overlay hides and Windows no longer shows Pisum Transcribe as using the microphone, before the process has ended
- **AND** nothing is inserted

#### Scenario: Exit while the microphone opens
- **WHEN** the overlay shows its starting look and the user chooses **Exit** before the microphone delivers audio
- **THEN** the overlay hides before the process has ended
- **AND** nothing is recorded or inserted

#### Scenario: Exit during transcription
- **WHEN** a dictation is being transcribed and the user chooses **Exit**
- **THEN** the overlay hides before the process has ended
- **AND** the process ends within 5 seconds
