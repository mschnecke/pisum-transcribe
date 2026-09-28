## Context

See proposal.md for the motivation and specs/settings-window/spec.md for the behavior.

**Current state:**
- The SDK writes `AssemblyInformationalVersionAttribute` as `<Version>+<commit>`, such as `1.5.0+fad2d9c…`. `<Version>` comes from `Directory.Build.props` on a local build and from the tag on a release (`-p:Version=<version>`), so a local build reads the same number as the release it follows.
- Two places read the attribute, each on its own:
  - `Program.cs:36` logs the full string at startup.
  - `UpdateCheckService`'s constructor strips everything from the `+` on, and parses the rest with `ReleaseVersion.ParseOwn`. Its `runningVersion` parameter replaces the attribute in tests, which pass `"1.1.1+b917e0ee"`, so the stripping applies to that value too.
- `SettingsViewModel` builds `GeneralSectionViewModel` from plain values: `(isStartAtSignInAvailable, startAtSignIn, requiresApproval, updates)`. No test constructs `GeneralSectionViewModel` directly.
- `SettingsDialog.axaml`'s general section is a `StackPanel` with the sign-in option, the "Check for updates automatically" check box and its hint (`TextBlock.hint`: wrapping, `SystemControlForegroundBaseMediumBrush`). `SettingsDialogTests` selects that section with `GeneralSection = 4`.

## Goals / Non-Goals

**Goals:**
- The version the settings window shows and the one the update check compares come from the same code, so they can't drift.
- The view model gets the version as a value, so a test can use a fixed one.

**Non-Goals:**
- Changing what `Program.cs` logs. The log keeps the commit, which is useful for dev builds.
- Showing the commit, even for dev builds. The log records it (see proposal.md, "Not in scope").

## Decisions

### D1: A static `AppVersion` in `Hosting/`

```
AssemblyInformationalVersion "1.5.0+fad2d9c"
          |
          v
 AppVersion.WithoutBuildMetadata(string?)  -->  "1.5.0"
 AppVersion.Current  (the app assembly's attribute, through it)
          |                                  |
          v                                  v
 UpdateCheckService                    SettingsViewModel
 runningVersion is null                  --> GeneralSectionViewModel(..., version)
   ? AppVersion.Current                         --> "Version 1.5.0"
   : WithoutBuildMetadata(runningVersion)
```

`internal static class AppVersion` with:
- `WithoutBuildMetadata(string? version)`: the text before the first `+`, or the whole text without a `+`. `null` stays `null`.
- `Current`: `WithoutBuildMetadata` of the informational version of the app assembly (`typeof(AppVersion).Assembly`), read once.

`UpdateCheckService` keeps its `runningVersion` parameter and its behavior. Only the stripping moves.

*Alternatives:* an `IAppVersion` service in DI. It would make the version injectable everywhere, but both readers already take it as a value (the update check's parameter, the view model's constructor), so an interface adds a registration and nothing to test. Leaving the update check alone and reading the attribute a third time in the settings window would let the two versions drift in format.

`Hosting/` is where app-wide infrastructure such as `AppPaths` lives. `Updates/` would make the settings window depend on the update feature for something that isn't about updates.

### D2: `GeneralSectionViewModel` takes the version as a constructor parameter

The new last parameter is `string? version`, and the view model exposes `VersionText` (`Version 1.5.0`) as a get-only property, not an `[ObservableProperty]`. It never changes while the window is open, so it raises no `PropertyChanged` and can't count as an edit (spec "Not an edit"). `SettingsViewModel` passes `AppVersion.Current`.

`VersionText` is `null` when the version is `null`, and the line is then hidden. The SDK always writes the attribute, so this only keeps a `null` from showing as `Version `, as the update check already copes with a missing version.

*Alternative:* `SettingsViewModel` taking the version as a parameter too. Its constructor comes from DI, so the value would need a registration or a factory. The dialog test can compare with `AppVersion.Current` instead.

### D3: A `SelectableTextBlock` below the update hint

```
[x] Check for updates automatically
    Asks GitHub once a day whether a new version ...
                                                     <- Margin top 16, as between the options
Version 1.5.0                                        <- SelectableTextBlock
```

The line uses a `SelectableTextBlock`, which Avalonia's Fluent theme supports with selection and Ctrl+C or Command+C. The foreground matches the hint's medium brush, and there's no bold, so it reads as information and not as an option. Avalonia's `TextBlock.hint` selector matches only the exact type `TextBlock`, so the line gets its own setters or a selector for `SelectableTextBlock`, not the `hint` class. The line has an `x:Name` (`VersionText`) for the dialog test.

*Alternatives:* a plain `TextBlock` with a **Copy** button (more UI for a rare action), or a read-only `TextBox` (looks like an input the user can edit).

## Risks / Trade-offs

- [A local build shows the same version as the release, such as `1.5.0`] → The log has the commit. A bug report from a local build is the developer's own, and the proposal leaves the commit out on purpose.
- [On macOS, `SelectableTextBlock` may not offer a context menu with **Copy**] → The keyboard shortcut copies. The spec asks only that the user can select and copy.
- [Moving the stripping changes `UpdateCheckService`] → `UpdateCheckServiceTests` pass `"1.1.1+b917e0ee"` and cover the comparison, so they catch a change in the stripped value.
