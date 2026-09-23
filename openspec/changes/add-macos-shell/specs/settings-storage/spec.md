## MODIFIED Requirements

### Requirement: Settings file location
User settings SHALL be stored as JSON in `%LOCALAPPDATA%\Pisum Transcribe\settings.json` on Windows and in `~/Library/Application Support/Pisum Transcribe/settings.json` on macOS.

#### Scenario: Settings are saved
- **WHEN** settings are saved on Windows
- **THEN** `%LOCALAPPDATA%\Pisum Transcribe\settings.json` contains the saved values as JSON

#### Scenario: Settings are saved on macOS
- **WHEN** settings are saved on macOS
- **THEN** `~/Library/Application Support/Pisum Transcribe/settings.json` contains the saved values as JSON
