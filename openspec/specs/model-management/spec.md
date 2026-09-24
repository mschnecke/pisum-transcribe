# model-management Specification

## Purpose

Defines which speech models Pisum Transcribe supports and how the user gets a verified copy of a model onto the machine before dictation can work.

## Requirements

### Requirement: Model catalog
The application SHALL provide a fixed catalog of supported speech models. Each entry SHALL define an identifier, display name, file size, SHA-256 hash, download source, supported source languages, supported translation directions, and license with attribution text. The catalog SHALL contain:

| Identifier | Model | Size | Languages |
|---|---|---|---|
| `canary-1b-v2-q8_0` | Canary 1B v2 (Q8_0) | 1,144,290,016 bytes | bg, cs, da, de, el, en, es, et, fi, fr, hr, hu, it, lt, lv, mt, nl, pl, pt, ro, ru, sk, sl, sv, uk |
| `canary-1b-v2-q4_k_m` | Canary 1B v2 (Q4_K_M) | 735,476,448 bytes | same as above |
| `canary-180m-flash-q8_0` | Canary 180M Flash (Q8_0) | 218,447,552 bytes | de, en, es, fr |

For every catalog model, translation SHALL be supported from each non-English language into English and from English into each non-English language.

#### Scenario: Catalog lists models
- **WHEN** the setup window is shown
- **THEN** it lists the three catalog models with display name, download size and supported languages

### Requirement: Selected model setting
The application SHALL persist the selected model as `model.selectedModelId`, with default `canary-1b-v2-q8_0`. An unknown identifier in the settings file SHALL be treated as the default.

#### Scenario: Default selection
- **WHEN** no model has been selected before
- **THEN** the selected model is `canary-1b-v2-q8_0`

#### Scenario: Unknown model identifier
- **WHEN** the settings file names a model identifier that is not in the catalog
- **THEN** the selected model is `canary-1b-v2-q8_0`

### Requirement: Installed model detection
A catalog model SHALL be considered installed when its file exists in the models folder, `%LOCALAPPDATA%\Pisum Transcribe\models\` on Windows and `~/Library/Application Support/Pisum Transcribe/models/` on macOS, and its size equals the catalog size. The check SHALL NOT recompute the hash, so startup stays fast.

#### Scenario: Model file present
- **WHEN** the model file exists in the models folder with the catalog size
- **THEN** the model is reported as installed

#### Scenario: Model file present on macOS
- **WHEN** the model file exists in `~/Library/Application Support/Pisum Transcribe/models/` on macOS with the catalog size
- **THEN** the model is reported as installed

#### Scenario: Truncated model file
- **WHEN** the model file exists but its size differs from the catalog size
- **THEN** the model is reported as not installed

### Requirement: Model download
Downloading a model SHALL fetch it over HTTPS from its pinned source, report progress (bytes received and the catalog size as total), and support cancellation. The partially downloaded data SHALL NOT be visible as an installed model at any point. If the source announces a file size that differs from the catalog size, or sends more data than the catalog size, the download SHALL fail and the model SHALL NOT be installed.

#### Scenario: Successful download
- **WHEN** the user starts downloading a model and the download completes with a matching SHA-256 hash
- **THEN** the model is reported as installed
- **AND** no temporary download file remains

#### Scenario: User cancels download
- **WHEN** the user cancels a running download
- **THEN** the download stops within 2 seconds
- **AND** the temporary download file is deleted
- **AND** the model is reported as not installed

#### Scenario: Network failure
- **WHEN** the connection fails during a download
- **THEN** the user sees an error message stating the download failed and can retry
- **AND** the temporary download file is deleted

#### Scenario: Unexpected download size
- **WHEN** the download source announces a file size that differs from the catalog size
- **THEN** the download fails before the file is received
- **AND** no temporary download file remains
- **AND** the user sees an error message stating the download failed and can retry

### Requirement: Integrity verification
After a download completes, the application SHALL compute the file's SHA-256 hash and compare it with the catalog value. On a mismatch, the file SHALL be deleted and the model SHALL NOT be installed.

#### Scenario: Hash mismatch
- **WHEN** a downloaded file's SHA-256 hash differs from the catalog value
- **THEN** the file is deleted
- **AND** the user sees an error saying the download was corrupted and can retry

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

### Requirement: Closing setup during a download
When the user closes the setup window while a download is running, the application SHALL ask for confirmation that closing cancels the download. If the user confirms, the download SHALL be cancelled and the window SHALL close. If the user declines, the download SHALL continue and the window SHALL stay open. When the application exits while a download is running, the download SHALL be cancelled without asking.

#### Scenario: User confirms closing
- **WHEN** a download is running, the user closes the setup window and confirms
- **THEN** the download stops within 2 seconds
- **AND** the setup window closes
- **AND** the model is reported as not installed

#### Scenario: User declines closing
- **WHEN** a download is running, the user closes the setup window and declines
- **THEN** the setup window stays open
- **AND** the download continues

#### Scenario: Application exits during a download
- **WHEN** a download is running and the user chooses **Exit** in the tray menu
- **THEN** no confirmation is shown
- **AND** the application ends within 5 seconds
- **AND** after the next start, the model is reported as not installed and no temporary download file remains

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

### Requirement: Model license attribution
The setup window SHALL show the license and attribution for each catalog model: the creator of the original model, the license name with a link to the license text, the changes made to the original model, and the source repository.

#### Scenario: Attribution shown
- **WHEN** the setup window is open
- **THEN** the creator, the license name with its link, the changes made and the source repository of the highlighted model are visible

### Requirement: Model deletion
An installed catalog model that is not the selected model SHALL be deletable. Deleting SHALL remove the model file, so the model is reported as not installed. The selected model SHALL NOT be deletable. If the model file cannot be deleted, for example because it is still in use, the model SHALL stay installed and the failure SHALL be reported to the user.

#### Scenario: Delete an unused model
- **WHEN** `canary-1b-v2-q4_k_m` is installed, it is not the selected model, and the user deletes it
- **THEN** its model file is removed
- **AND** the model is reported as not installed

#### Scenario: Selected model cannot be deleted
- **WHEN** a request to delete the selected model is made
- **THEN** the request is rejected
- **AND** the model file is kept

#### Scenario: Model file in use
- **WHEN** the user deletes a model whose file cannot be removed because it is in use
- **THEN** the model is still reported as installed
- **AND** the user is told that the model could not be deleted

### Requirement: One download per model
At most one download of a model SHALL run at a time. Starting a download of a model that is already downloading SHALL be rejected with an error saying that the model is already downloading. The running download SHALL continue unaffected.

#### Scenario: Same model started twice
- **WHEN** a download of `canary-180m-flash-q8_0` is running and another download of the same model is started
- **THEN** the second download is rejected with an error saying the model is already downloading
- **AND** the first download continues
- **AND** the model is reported as installed once the first download succeeds

### Requirement: Models folder excluded from backups
On macOS, the application SHALL exclude the models folder from Time Machine backups. The exclusion SHALL be in place at every start and before every download, so that it also covers a models folder created by an earlier version, and it SHALL cover every model in the folder. The settings file and the logs SHALL stay in backups.

#### Scenario: Model not backed up
- **WHEN** a model has been downloaded on macOS
- **THEN** `tmutil isexcluded` reports the models folder as excluded
- **AND** it doesn't report the folder that holds `settings.json` as excluded

#### Scenario: Folder from an earlier version
- **WHEN** the models folder was created by a version without the exclusion and Pisum Transcribe starts
- **THEN** the models folder is excluded from Time Machine
