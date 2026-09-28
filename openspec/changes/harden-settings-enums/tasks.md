`dotnet test Pisum.Transcribe.slnx` must pass after every group, on Windows locally and on both platforms in CI.

## 1. Unknown enum values take their default

- [x] 1.1 Add the `internal static` table of enum settings to `JsonSettingsStore`: `transcription.backend` (`BackendPreference`), `transcription.task` (`TranscriptionTask`) and `textInsertion.method` (`InsertionMethod`) (D2). Add the step after `Migrate` in `Read()` that removes each listed property whose value isn't a string equal to one of `Enum.GetNames(type)` (ignoring case), and logs one warning with the dotted setting path and the value's `ToJsonString()` (D1, D3). A missing section or property, a section that isn't an object, and a `null` section are left alone. Verify: `dotnet build Pisum.Transcribe.slnx` passes for both frameworks.
- [x] 1.2 Set `allowIntegerValues: false` on the `JsonStringEnumConverter` in `SerializerOptions` (D4). Verify: the existing round-trip tests in `JsonSettingsStoreTests` still pass, so writing is unchanged.
- [x] 1.3 Add the tests to `JsonSettingsStoreTests` (spec `settings-storage` "Partial settings files"):
  - one theory for each enum setting over `"metal"`-style unknown names, `null`, a number, `true`, `{}`, `[]` and `"gpu, cpu"`-style lists: the setting takes its default, and no `.corrupt` file exists
  - a file with `"backend": "metal"` and custom hotkey, languages, model, task and insertion method: the backend is `Auto` and every other value is kept
  - `"backend": "GPU"` loads as `Gpu`
  - the warning names `transcription.backend` and `"metal"` (with `CapturingLogger`)
  - the file's bytes are unchanged after `Load()`, and the next `SaveAsync` writes `"auto"`
  - a format 1 file with `"vulkan"` still loads as `Gpu` (the step runs after the migration)
  - `"schemaVersion": "1"` and `"transcription": 5` still rename the file to `.corrupt` (spec "Recovery from corrupt settings")

  Verify: `dotnet test Pisum.Transcribe.slnx --filter-class "*.JsonSettingsStoreTests"` passes.
- [x] 1.4 Rewrite `Load_UnknownBackendOrNotMigrated_RenamesFileAndUsesDefaults`: the `"metal"` and the version 3 `"vulkan"` cases now load the backend as `Auto`, keep the file and log the warning. The `"schemaVersion": "1"` case moves to the corrupt-file tests from 1.3. Verify: the class passes, and no test still expects `.corrupt` for an unknown enum value.
- [x] 1.5 Add the reflection guard test (D2): it collects every enum or nullable-enum property of every section record under `AppSettings` as its camelCase path with its type, and asserts that this set equals the store's table. Verify: the test passes, and it fails when an entry is temporarily removed from the table.
- [x] 1.6 Update the XML doc of `AppSettings`: the **Renames** bullet says that a renamed enum value without a migration step now loses its value (it takes the default) instead of making the file corrupt, and a new bullet says that a new enum setting goes into `JsonSettingsStore`'s table, which the guard test checks. Verify: `dotnet build` passes (`GenerateDocumentationFile` with warnings as errors).

## 2. Docs and wrap-up

- [x] 2.1 Update the docs:
  - `CLAUDE.md`: the **Settings** paragraph says that an unknown enum value takes its default with a warning, and that a new enum setting goes into the table in `JsonSettingsStore`
  - `docs/roadmap.md`: GitHub #32 done, in place of "has no OpenSpec change yet"

  Verify: the two files mention the fallback, and no text still says that an unknown enum value makes the file corrupt (`grep -rn "corrupt" CLAUDE.md docs/`).
- [ ] 2.2 Final check. Verify: `openspec validate harden-settings-enums --strict` passes, and `dotnet build Pisum.Transcribe.slnx` and `dotnet test Pisum.Transcribe.slnx` pass on Windows. CI passes on Windows and macOS on the PR, which references #32 without a closing keyword.
