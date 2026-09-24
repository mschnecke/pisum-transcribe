## ADDED Requirements

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
