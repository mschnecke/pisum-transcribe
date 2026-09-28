`dotnet test Pisum.Transcribe.slnx` must pass after every group, on Windows locally and on both platforms in CI.

## 1. One place reads the version

- [x] 1.1 Add `Hosting/AppVersion.cs`: `internal static class AppVersion` with `WithoutBuildMetadata(string?)` (the text before the first `+`, `null` stays `null`) and `Current` (that of the app assembly's `AssemblyInformationalVersionAttribute`, read once), with XML docs (D1). Verify: `dotnet build Pisum.Transcribe.slnx` passes for both frameworks.
- [x] 1.2 Add `tests/Pisum.Transcribe.Tests/Hosting/AppVersionTests.cs` (`Unit`): `"1.5.0+fad2d9c"` gives `"1.5.0"`, `"1.4.0-rc.1+abc"` gives `"1.4.0-rc.1"`, `"1.5.0"` stays as it is, `null` gives `null`, and `Current` is not `null` and contains no `+`. Verify: `dotnet test Pisum.Transcribe.slnx --filter-class "*.AppVersionTests"` passes.
- [x] 1.3 Make `UpdateCheckService`'s constructor use `AppVersion`: `runningVersion is null ? AppVersion.Current : AppVersion.WithoutBuildMetadata(runningVersion)`, and remove the `using` that only the old read needed (D1). Verify: `dotnet test Pisum.Transcribe.slnx --filter-class "*.UpdateCheckServiceTests"` passes unchanged, with its `"1.1.1+b917e0ee"`.

## 2. The version line in the general section

- [x] 2.1 Add the parameter `string? version` to `GeneralSectionViewModel`'s constructor and the get-only property `VersionText` (`$"Version {version}"`, or `null` without a version), with XML docs, and pass `AppVersion.Current` from `SettingsViewModel` (D2). Update the class's summary to mention the version. Verify: `dotnet build Pisum.Transcribe.slnx` passes.
- [x] 2.2 Add the line to the general section of `SettingsDialog.axaml`, after the update hint: a `SelectableTextBlock` named `VersionText`, bound to `VersionText`, with a top margin of 16, the hint's medium foreground brush and wrapping, and hidden while `VersionText` is `null` (D3). Verify: `dotnet build Pisum.Transcribe.slnx` passes (compiled bindings).
- [x] 2.3 Add tests (spec `settings-window` "Version information"):
  - `SettingsDialogTests`: with the general section selected, `VersionText` is visible, is a `SelectableTextBlock`, and shows `$"Version {AppVersion.Current}"`
  - `SettingsViewModelTests`: after construction `General.VersionText` is `$"Version {AppVersion.Current}"` and **Save** can't run (not an edit)
  - a `GeneralSectionViewModel` test with fixed values: `"1.4.0-rc.1"` gives `Version 1.4.0-rc.1`, `null` gives `null`

  Verify: `dotnet test Pisum.Transcribe.slnx --filter-class "*.SettingsDialogTests"` and the other two classes pass.
- [ ] 2.4 Check by hand on Windows: `dotnet run --project src/Pisum.Transcribe -f net10.0-windows10.0.19041.0`, open **Settings…** → **General**. Verify: the line reads `Version 1.5.0` below the update hint, and selecting it and pressing Ctrl+C copies it. (macOS: the same with Command+C, checked by hand on a Mac; noted on the PR if not done.)

## 3. Docs and wrap-up

- [x] 3.1 Update `docs/roadmap.md`: add GitHub #39 `add-settings-version-info` to the table, the dependency graph and the status notes, as done for #32. Verify: `grep -n "#39" docs/roadmap.md` shows the row and the note.
- [ ] 3.2 Final check. Verify: `openspec validate add-settings-version-info --strict` passes, and `dotnet build Pisum.Transcribe.slnx` and `dotnet test Pisum.Transcribe.slnx` pass on Windows. CI passes on Windows and macOS on the PR, which references #39 without a closing keyword.
