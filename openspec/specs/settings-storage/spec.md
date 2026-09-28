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
When the settings file lacks some settings, contains unknown properties, or stores a value that the current version doesn't know, known settings SHALL be read from the file, missing settings SHALL take their default values, and unknown properties SHALL be ignored.

A setting that takes one of a fixed set of named values (the backend, the task and the insertion method) SHALL take its default value when the file stores anything other than one of those names, compared without regard to case: an unknown name, `null`, a number, a boolean, an object, an array or a list of several names. Each such value SHALL be logged as a warning that names the setting and the value from the file. The file SHALL NOT be written when it is read; the next time settings are saved, the default is written in place of the unknown value.

#### Scenario: File from an older version
- **WHEN** the settings file lacks a setting that the current version defines
- **THEN** that setting takes its default value
- **AND** all other settings keep the values from the file

#### Scenario: Unknown value from a newer version
- **WHEN** the application starts with a settings file that stores the backend `metal`, and custom values for the hotkey, the languages, the model and the insertion method
- **THEN** the backend takes its default value `auto`
- **AND** every other setting keeps its value from the file
- **AND** the file is not renamed to `settings.json.corrupt`
- **AND** a warning that names the backend setting and the value `metal` is written to the log

#### Scenario: Value that is not a name
- **WHEN** the settings file stores `null`, a number or a list of several names as the backend, the task or the insertion method
- **THEN** that setting takes its default value
- **AND** every other setting keeps its value from the file

#### Scenario: Name in another case
- **WHEN** the settings file stores the backend as `GPU`
- **THEN** the backend is `gpu`

#### Scenario: No write for an unknown value
- **WHEN** the application starts with a settings file that stores an unknown backend and the user saves no settings
- **THEN** the file is left unchanged

### Requirement: Recovery from corrupt settings
When the settings file cannot be parsed, the application SHALL rename it to `settings.json.corrupt`, replacing any existing file with that name, log a warning, and continue with default values. A file counts as unparseable when it is not valid JSON, or when its content, a section or a setting has the wrong kind of value, such as a text where a number is expected. A value of a named-value setting that the current version doesn't know SHALL NOT make the file count as unparseable (see "Partial settings files").

#### Scenario: Corrupt settings file
- **WHEN** the application starts and `settings.json` contains invalid JSON
- **THEN** the file is renamed to `settings.json.corrupt`
- **AND** the application starts with default settings
- **AND** a warning is written to the log

#### Scenario: Wrong kind of value
- **WHEN** the application starts and `settings.json` stores its format version as a text
- **THEN** the file is renamed to `settings.json.corrupt`
- **AND** the application starts with default settings

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
