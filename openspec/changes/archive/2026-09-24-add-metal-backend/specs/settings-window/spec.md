## MODIFIED Requirements

### Requirement: Backend settings
The engine section SHALL let the user choose the backend preference Auto, the GPU or CPU, and SHALL show the backend currently in use or the engine status if it is not ready. The GPU option SHALL be named for the platform's GPU backend: Vulkan (GPU) on Windows and Metal (GPU) on macOS.

#### Scenario: Current backend displayed
- **WHEN** the engine is ready on the GPU backend
- **THEN** the engine section shows that the GPU backend is in use, by its name

#### Scenario: GPU option on Windows
- **WHEN** on Windows the user opens the engine section
- **THEN** the GPU option is named Vulkan (GPU)

#### Scenario: GPU option on macOS
- **WHEN** on macOS the user opens the engine section
- **THEN** the GPU option is named Metal (GPU)
- **AND** when the engine is ready on the GPU backend, the section shows that Metal is in use

### Requirement: Apply changes without restart
Saved changes SHALL take effect without restarting the application:
- A new hotkey SHALL be active immediately after saving. If the previous hotkey is held when saving, that dictation SHALL be cancelled and nothing SHALL be inserted.
- A change of active model or backend preference SHALL reload the engine in the background. The engine status SHALL be `Loading` during the reload, and dictation SHALL be unavailable until it is `Ready`.
- Task, language and text insertion changes SHALL apply to the next dictation. A dictation in progress SHALL keep the task, language and text insertion settings it started with.
- A dictation that is already being transcribed when a model or backend change is saved SHALL complete on the previous model before the reload starts.
- A recording that is still running when a model or backend change is saved SHALL be transcribed only if the reload has finished when the hotkey is released. Otherwise it SHALL NOT be transcribed, and the user SHALL be notified that the model is still loading.
- Turning "Check for updates automatically" off SHALL stop further update checks and hide an update notice that is shown. Turning it on SHALL start an update check within 1 minute.

#### Scenario: New hotkey without restart
- **WHEN** the user changes the hotkey from right Ctrl to left Ctrl+left Win and saves
- **THEN** holding left Ctrl+left Win starts a dictation
- **AND** holding right Ctrl alone no longer does

#### Scenario: Backend switch reloads engine
- **WHEN** the engine is ready on the GPU backend and the user saves backend CPU
- **THEN** the engine status becomes `Loading` and then `Ready` on CPU
- **AND** the open settings window shows the status change
- **AND** the application did not restart

#### Scenario: Language change applies to next dictation
- **WHEN** the user saves task Transcribe with source German
- **THEN** the next dictation inserts German text

#### Scenario: Transcription during a model change
- **WHEN** a dictation is being transcribed and the user saves a different installed model
- **THEN** the dictation's text is inserted
- **AND** the engine then reloads with the new model

#### Scenario: Recording during a model change
- **WHEN** the user holds the hotkey, saves a different installed model, and releases the hotkey while the reload is still running
- **THEN** nothing is inserted
- **AND** a notification says the model is still loading

#### Scenario: Old hotkey held while saving a new one
- **WHEN** a recording is running because the user holds right Ctrl, and the user saves left Ctrl+left Win as the new hotkey
- **THEN** the recording is cancelled
- **AND** nothing is inserted

#### Scenario: Update check turned on without restart
- **WHEN** "Check for updates automatically" is off, and the user turns it on and saves
- **THEN** an update check runs within 1 minute
- **AND** the application did not restart

#### Scenario: Update check turned off while a notice is shown
- **WHEN** the tray menu shows **Pisum Transcribe 1.2.0 is available…**, and the user turns off "Check for updates automatically" and saves
- **THEN** the tray menu no longer shows the update item
- **AND** no further update check runs
