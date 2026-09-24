## MODIFIED Requirements

### Requirement: Disk space check
Before starting a download, the application SHALL check that the models folder's volume has at least the model size plus 100 MB free. If it does not, the application SHALL NOT start the download and SHALL tell the user how much space is needed. On macOS, the free space SHALL be the capacity the system makes available for a download the user starts, which includes space the system can free, such as purgeable caches and optimized iCloud Drive storage, as Finder shows it.

#### Scenario: Not enough disk space
- **WHEN** the user starts a download of a 1,144,290,016-byte model and the volume has 800 MB free
- **THEN** no download starts
- **AND** the user sees a message stating the required free space

#### Scenario: Purgeable space counts on macOS
- **WHEN** the user starts a download of a 1,144,290,016-byte model on macOS, the volume has 800 MB of unused space, and the system can make 5 GB available for the download
- **THEN** the download starts

### Requirement: First-run setup
When the application starts and the selected model is not installed, it SHALL open a setup window. On macOS, it SHALL also open the setup window when Accessibility or the microphone permission is not granted (see `macos-permissions`). The window SHALL let the user choose a catalog model (preselected: the selected model), start the download, watch progress, cancel, and retry after an error. Starting a download SHALL save the chosen model as the selected model before any model data is received, so that the model is already selected when it becomes installed. On Windows, the window SHALL close after a successful download. On macOS, the window SHALL close once the selected model is installed and Accessibility and the microphone permission are granted, in whichever order they are completed; the optional permissions SHALL NOT keep it open.

#### Scenario: First start without a model
- **WHEN** the application starts and the selected model is not installed
- **THEN** the setup window opens with the selected model preselected

#### Scenario: User picks a smaller model
- **WHEN** the user selects `canary-180m-flash-q8_0` in the setup window and starts the download
- **THEN** `model.selectedModelId` is saved as `canary-180m-flash-q8_0`
- **AND** the setup window closes after the download succeeds

#### Scenario: User cancels after picking a model
- **WHEN** the user selects `canary-180m-flash-q8_0`, starts the download and cancels it
- **THEN** `model.selectedModelId` stays `canary-180m-flash-q8_0`
- **AND** the model is reported as not installed
- **AND** the next time the setup window opens, `canary-180m-flash-q8_0` is preselected

#### Scenario: Model installed but a permission missing on macOS
- **WHEN** Pisum Transcribe starts on macOS, the selected model is installed and the microphone permission is not granted
- **THEN** the setup window opens

#### Scenario: Download finishes before the permissions on macOS
- **WHEN** the download succeeds on macOS while Accessibility is not granted
- **THEN** the setup window stays open
- **AND** it closes once Accessibility is granted

#### Scenario: Optional permissions don't hold the window on macOS
- **WHEN** on macOS the selected model is installed and both required permissions are granted, and the user hasn't answered the notification permission
- **THEN** the setup window closes

### Requirement: Reopening setup from the tray
While the selected model is not installed, the tray menu SHALL contain a **Download model…** item that opens the setup window. While the selected model is installed, the item SHALL be hidden. Whether the item is shown SHALL reflect the installed state at the moment the tray menu opens, including when a model file was removed while the application runs. On macOS, when the application runs as an app bundle, the **Set up Pisum Transcribe…** item takes the place of **Download model…** and is also shown while a required permission is missing (see `macos-permissions`); there the scenarios below hold for that item.

#### Scenario: User closed setup without downloading
- **WHEN** the user closes the setup window without downloading
- **THEN** the application keeps running in the tray
- **AND** the tray menu shows **Download model…**

#### Scenario: Selected model installed
- **WHEN** the download of the selected model succeeds
- **THEN** the next time the user opens the tray menu, it does not show **Download model…**

#### Scenario: Model file removed while running
- **WHEN** the selected model is installed and its file is deleted while the application runs
- **THEN** the next time the user opens the tray menu, it shows **Download model…**

#### Scenario: Combined item on macOS
- **WHEN** the application runs on macOS as an app bundle and the user closes the setup window without downloading
- **THEN** the menu bar menu shows **Set up Pisum Transcribe…**
- **AND** it doesn't show **Download model…**

## ADDED Requirements

### Requirement: Models folder excluded from backups
On macOS, the application SHALL exclude the models folder from Time Machine backups. The exclusion SHALL be in place at every start and before every download, so that it also covers a models folder created by an earlier version, and it SHALL cover every model in the folder. The settings file and the logs SHALL stay in backups.

#### Scenario: Model not backed up
- **WHEN** a model has been downloaded on macOS
- **THEN** `tmutil isexcluded` reports the models folder as excluded
- **AND** it doesn't report the folder that holds `settings.json` as excluded

#### Scenario: Folder from an earlier version
- **WHEN** the models folder was created by a version without the exclusion and Pisum Transcribe starts
- **THEN** the models folder is excluded from Time Machine
