## Why

`JsonSettingsStore` reads enum settings with `JsonStringEnumConverter`, which throws on a value it doesn't know. "Recovery from corrupt settings" then renames the whole file to `settings.json.corrupt`, so one unknown value costs the user their hotkey, languages, model and every other setting. It happens whenever a build reads a file written by a newer one: a manual downgrade after `add-metal-backend`, which stores the backend as `"gpu"`, or any later change that adds or renames an enum value.

Tracked in issue #32. The decisions come from explore mode on 2026-09-28. Builds that are already released can't be fixed; this protects the builds from this change on.

## What Changes

- **An unknown enum value takes that setting's default.** Any value of `transcription.backend`, `transcription.task` or `textInsertion.method` that isn't one of the enum's names (read without regard to case) falls back to the default: an unknown string, `null`, a number, a boolean, an object, an array or a comma list such as `"gpu, cpu"`. The other settings are read from the file as usual.
- **One warning per dropped value.** It names the setting and the value from the file, such as `transcription.backend` and `"metal"`. It never contains transcript data.
- **The file isn't renamed and isn't written at startup.** The next save writes it in the current format, as the format migration already does.
- **Numbers are no longer read as enum values.** Today `"backend": 1` quietly means `gpu`, and `"backend": 7` loads as a value the enum doesn't define. The app has always written names, so numbers only come from hand edits.
- **Still corrupt:** a file that isn't valid JSON, or whose root, a section or a non-enum property has the wrong shape, such as `"schemaVersion": "1"`.
- **A guard for the future:** a test fails when an enum setting is added to `AppSettings` without being checked.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `settings-storage`:
  - "Partial settings files": a value that the current version doesn't know takes its default, next to missing settings and unknown properties.
  - "Recovery from corrupt settings": an unknown enum value no longer makes the file count as corrupt.

## Impact

- **Depends on:** `add-metal-backend` (#18), which is merged and archived. Its `JsonNode` pre-pass is where the new check goes.
- **Code:** `Settings/JsonSettingsStore.cs` (the check after the migration, `AllowIntegerValues = false` on the converter) and the rules in the XML doc of `Settings/AppSettings.cs`.
- **Tests:** `JsonSettingsStoreTests` gain the unknown-value cases and the reflection guard. `Load_UnknownBackendOrNotMigrated_RenamesFileAndUsesDefaults` changes: an unknown backend now falls back to `auto` and keeps the other settings.
- **Settings:** no format change, so `AppSettings.CurrentSchemaVersion` stays 2.
- **Docs:** `docs/roadmap.md` (the change done) and the **Settings** paragraph of `CLAUDE.md`.
- **Issue:** the PR references #32 without a closing keyword; the issue is closed when the change is done.
