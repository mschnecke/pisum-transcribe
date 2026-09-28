## Context

See proposal.md for the motivation and specs/settings-storage/spec.md for the behavior.

**Current state:**
- `JsonSettingsStore.Read()` parses the file into a `JsonNode`, runs `Migrate` (format 1 to 2: `"vulkan"` to `"gpu"`), and deserializes the node with `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`. Any `JsonException` renames the file to `.corrupt` and returns the defaults.
- Three settings are enums, all constructor parameters with defaults on their section records: `transcription.backend` (`BackendPreference`, default `Auto`), `transcription.task` (`TranscriptionTask`, default `Translate`) and `textInsertion.method` (`InsertionMethod`, default `ClipboardPaste`). System.Text.Json uses a parameter's default when the property is missing from the JSON.
- How the converter reads, as probed on .NET 10 on 2026-09-28:

  | Value | Default converter | `allowIntegerValues: false` |
  |---|---|---|
  | `"gpu"`, `"GPU"`, `"Gpu"` | `Gpu` | `Gpu` |
  | `"gpu, cpu"` | `3` (undefined) | `3` (undefined) |
  | `1`, `"1"` | `Gpu` | `JsonException` |
  | `7` | `7` (undefined) | `JsonException` |
  | `null`, `"metal"` | `JsonException` | `JsonException` |

  The archived `add-metal-backend` design says the converter reads case-sensitively. It doesn't; only `Migrate`'s own `"vulkan"` comparison is case-sensitive.

## Goals / Non-Goals

**Goals:**
- An enum value that isn't one of the enum's names never reaches the converter, so it can neither reset the file nor load as an undefined value.
- A new enum setting that is forgotten in the check fails a test, not a user's settings.

**Non-Goals:**
- Unknown values of string settings. The model id (`ModelCatalog.Resolve`) and the hotkey (`HotkeyParser.Parse`) already treat unknown values as the default where they're used.
- Changing the format version or the migration. No value is renamed.
- A general validation framework for settings.

## Decisions

### D1: Drop the property in the `JsonNode` pre-pass, after the migration

`Read()` gets one more step between `Migrate` and `Deserialize`:

```
file --> JsonNode.Parse --> Migrate --> DropUnknownEnumValues --> Deserialize<AppSettings>
                                          |
                          for each (section, property, enum type) on the list:
                            section missing or not an object  --> skip (deserializer decides)
                            property missing                  --> skip
                            value is a string that equals one
                            of Enum.GetNames(type), ignoring case --> keep
                            anything else                     --> log warning, remove property
```

Removing the property makes the deserializer use the parameter's default, the same path as a missing setting. So the defaults are defined in one place, the section records.

- The step runs after `Migrate`, so a format 1 `"vulkan"` is renamed to `"gpu"` first and kept.
- A section that isn't an object (such as `"transcription": 5`) is left alone and still makes the file corrupt, as it does today. A section set to `null` is left alone and replaced by its default after loading, as it is today.
- The check compares with `Enum.GetNames` using `StringComparison.OrdinalIgnoreCase`. The camelCase names written by the app and the PascalCase C# names differ only in case. Exact name matching, not `Enum.TryParse`, rejects `"1"` and `"gpu, cpu"`.

*Rejected:* a tolerant converter that returns a fallback for unknown values. A converter sees the enum type, not the property, so it can't know the per-setting default: `default(TEnum)` happens to be right for all three enums today, but only by the order of their members. It would also have to swallow reader errors inside the converter.

### D2: A hand-written list, guarded by a reflection test

`JsonSettingsStore` holds the list of enum settings as an `internal static` table of `(section, property, enum type)`, such as `("transcription", "backend", typeof(BackendPreference))`. Its style matches `Migrate`: explicit JSON paths, no reflection in the app.

A unit test walks `AppSettings` with reflection: every section property, and every property of each section record whose type is an enum (or a nullable enum). It turns each into its camelCase JSON path and asserts that the set equals the table's set, with the same enum types. Adding an enum setting without a table entry fails that test. The `AppSettings` XML doc gets a rule that says so.

*Rejected:* reflection in the store itself. It covers new enums automatically, but it puts about 20 lines of indirection into startup code for three entries that rarely change, and the test gives the same safety.

### D3: One warning per dropped value, with the raw token

The warning reads like `The setting {Setting} has the unknown value {Value} and uses its default`. `{Setting}` is the dotted path (`transcription.backend`), and `{Value}` is the node's `ToJsonString()`, or `null` for a JSON `null`. The value comes from the settings file, never from a transcript, so the logging rule holds. The warning repeats on every start until the user saves, as the migration's information message does.

### D4: `AllowIntegerValues = false` on the converter

After D1, only names reach the converter for listed properties. Turning integers off makes an enum property that is missing from the list fail loudly (the file counts as corrupt) instead of loading an undefined value. The app only ever wrote names, so no real file changes meaning. Writing is unaffected.

### D5: Nothing is written at load

As with the migration (`add-metal-backend` D4), `Load()` doesn't write the file. `Current` holds the default in place of the unknown value, and the next `SaveAsync` writes it. A user who goes back to the newer build without saving finds their value intact.

## Risks / Trade-offs

- [After a downgrade, a save writes the default over the newer build's value, such as `"gpu"` becoming `"auto"`] → That's the setting's own default, and only this one setting changes. Before, every setting was lost. A user who doesn't save keeps the newer value (D5).
- [A future enum setting is added without a table entry] → The reflection test fails (D2). If a test is skipped, D4 makes a number or an unknown name count as corrupt rather than loading an undefined value, which is today's behavior.
- [A hand-edited format 1 file with `"Vulkan"` in another case] → `Migrate` compares case-sensitively, so it isn't renamed, and D1 drops it to `auto` instead of `gpu`. Before this change the file counted as corrupt. The app always wrote lowercase, so this doesn't happen with real files, and the change leaves `Migrate` alone.
- [A large object or array logged as the value] → Only a hand edit produces one, and the log file is local and rotates after 7 days.
