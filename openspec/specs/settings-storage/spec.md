# settings-storage Specification

## Purpose

Defines how user settings are saved to and loaded from disk, so preferences survive restarts and a damaged file never stops the application from starting.

## Requirements

### Requirement: Settings file location
User settings SHALL be stored as JSON in `%LOCALAPPDATA%\Pisum Transcribe\settings.json` on Windows and in `~/Library/Application Support/Pisum Transcribe/settings.json` on macOS.

#### Scenario: Settings are saved
- **WHEN** settings are saved on Windows
- **THEN** `%LOCALAPPDATA%\Pisum Transcribe\settings.json` contains the saved values as JSON

#### Scenario: Settings are saved on macOS
- **WHEN** settings are saved on macOS
- **THEN** `~/Library/Application Support/Pisum Transcribe/settings.json` contains the saved values as JSON

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

### Requirement: Settings format migration
The settings file SHALL record the version of its format. When the application reads a file of an older format, or a file without a version, it SHALL migrate the file's content to the current format before it reads the settings, and such a file SHALL NOT count as unparseable for a value that the migration renames. The migration SHALL NOT write the file: the migrated settings SHALL be written in the current format, with the current version, the next time settings are saved. Format 2 renames the backend value `vulkan` to `gpu`.

#### Scenario: Backend value from format 1
- **WHEN** the application starts with a settings file that has no version or version 1, and stores the backend `vulkan`
- **THEN** the backend is `gpu`
- **AND** every other setting keeps its value from the file
- **AND** the file is not renamed to `settings.json.corrupt`

#### Scenario: No write at startup
- **WHEN** the application starts with a settings file of format 1 and the user saves no settings
- **THEN** the file is left unchanged

#### Scenario: Migrated file written on the next save
- **WHEN** the application started with a settings file of format 1, and the user then saves settings
- **THEN** the file stores version 2 and the backend `gpu`
