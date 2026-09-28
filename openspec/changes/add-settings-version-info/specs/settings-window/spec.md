## ADDED Requirements

### Requirement: Version information
The general section SHALL show the version of the running application as `Version <version>`, below the option "Check for updates automatically" and its hint. `<version>` SHALL be the application's version without build metadata: the text after a `+`, such as the commit, SHALL NOT be shown, and a pre-release suffix such as `-rc.1` SHALL be shown. It SHALL be the same version the update check compares with a release. The user SHALL be able to select and copy the text. The line SHALL be read-only, SHALL NOT count as an edit, and SHALL be the same on Windows and macOS.

#### Scenario: Release version
- **WHEN** version 1.5.0 is running, built from commit `fad2d9c`, and the user opens the general section of the settings window
- **THEN** the section shows `Version 1.5.0`
- **AND** the text does not contain `fad2d9c`

#### Scenario: Pre-release version
- **WHEN** version 1.4.0-rc.1 is running and the user opens the general section
- **THEN** the section shows `Version 1.4.0-rc.1`

#### Scenario: Copying the version
- **WHEN** the user selects the version line and copies it
- **THEN** the clipboard contains `Version 1.5.0`, or the part of it that the user selected

#### Scenario: Not an edit
- **WHEN** the user opens the settings window and only selects the version line
- **THEN** **Save** stays disabled
