## Why

The settings window doesn't show which version of Pisum Transcribe is running. A user who reports a bug or wonders whether the update notice applies to them has no place in the app to look it up; today the version is only in the log file and, on Windows, in the list of installed apps.

Tracked in issue #39. The decisions come from explore mode on 2026-09-28.

## What Changes

- **A version line in the general section.** Below "Check for updates automatically" and its hint, the section shows `Version <version>`, such as `Version 1.5.0` or `Version 1.4.0-rc.1`. It's the same on Windows and macOS.
- **Without the build metadata.** The line shows the version the update check compares, the informational version without the `+<commit>` that the SDK appends. The log keeps the full string.
- **Selectable text.** The user can select and copy the line, for example into a bug report. There is no copy button.
- **One place reads the version.** The update check and the settings window read the running version through one shared helper instead of each reading the assembly attribute.

Not in scope: an About section, links to the repository or the release page, the license and notices, the update state next to the version, and the commit of a dev build.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `settings-window`: a new requirement "Version information": the general section shows the running version without build metadata, as selectable text.

## Impact

- **Code:** a new `Hosting/AppVersion.cs`, which reads the informational version without the build metadata. `Updates/UpdateCheckService.cs` uses it for its default running version. `SettingsWindow/GeneralSectionViewModel.cs` takes the version, `SettingsWindow/SettingsViewModel.cs` passes it, and `SettingsWindow/SettingsDialog.axaml` shows the line.
- **Tests:** `SettingsDialogTests` (the line in the general section), `GeneralSectionViewModel` or `SettingsViewModel` tests for the text, and tests of `AppVersion`'s stripping. `UpdateCheckServiceTests` keep passing their own running version.
- **Settings:** none. The line is read-only, so the settings file and `AppSettings.CurrentSchemaVersion` stay unchanged.
- **Platforms:** shared code only, no `Windows/` or `MacOS/` code and no Swift helper change.
- **Docs:** `docs/roadmap.md` (issue #39 and its change).
- **Issue:** the PR references #39 without a closing keyword; the issue is closed when the change is done.
