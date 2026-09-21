## Purpose

Defines how user settings are saved to and loaded from disk, so preferences survive restarts and a damaged file never stops the application from starting.

## ADDED Requirements

### Requirement: Settings file location
User settings SHALL be stored as JSON in `%LOCALAPPDATA%\Pisum Transcribe\settings.json`.

#### Scenario: Settings are saved
- **WHEN** settings are saved
- **THEN** `%LOCALAPPDATA%\Pisum Transcribe\settings.json` contains the saved values as JSON

### Requirement: Defaults when no settings exist
When the settings file does not exist, the application SHALL use default values for every setting and SHALL NOT fail to start.

#### Scenario: First start
- **WHEN** the application starts and no settings file exists
- **THEN** every setting has its documented default value

### Requirement: Partial settings files
When the settings file lacks some settings or contains unknown properties, known settings SHALL be read from the file, missing settings SHALL take their default values, and unknown properties SHALL be ignored.

#### Scenario: File from an older version
- **WHEN** the settings file lacks a setting that the current version defines
- **THEN** that setting takes its default value
- **AND** all other settings keep the values from the file

### Requirement: Recovery from corrupt settings
When the settings file cannot be parsed, the application SHALL rename it to `settings.json.corrupt`, replacing any existing file with that name, log a warning, and continue with default values.

#### Scenario: Corrupt settings file
- **WHEN** the application starts and `settings.json` contains invalid JSON
- **THEN** the file is renamed to `settings.json.corrupt`
- **AND** the application starts with default settings
- **AND** a warning is written to the log

### Requirement: Safe settings writes
Saving settings SHALL NOT leave a truncated or half-written `settings.json` behind, even if the process ends during the save.

#### Scenario: Process ends during save
- **WHEN** the process is terminated while settings are being written
- **THEN** `settings.json` contains either the previous or the new complete settings
