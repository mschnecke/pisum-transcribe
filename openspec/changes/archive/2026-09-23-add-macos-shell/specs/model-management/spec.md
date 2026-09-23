## MODIFIED Requirements

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
