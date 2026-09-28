## MODIFIED Requirements

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
