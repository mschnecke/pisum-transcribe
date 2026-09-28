`dotnet test Pisum.Transcribe.slnx` must pass after every group, on Windows locally and on both platforms in CI.

## 1. Checked items in the tray menu

- [x] 1.1 Add the optional parameter `Func<bool>? isChecked = null` to both `ITrayIconService.AddMenuItem` overloads, with XML docs (a radio item whose check state is read each time the menu opens). In `TrayIconService`, set `ToggleType = Radio` for an item with `isChecked`, keep it in the `_menuItems` tuple, and set `IsChecked` in `UpdateMenuItems` for shown items (D1). Verify: `dotnet build Pisum.Transcribe.slnx` passes for both frameworks.
- [x] 1.2 Add `TrayIconServiceTests`: an item with `isChecked` is a radio item whose `IsChecked` follows the function on each `UpdateMenuItems`; an item without it stays `ToggleType.None`. Verify: `dotnet test Pisum.Transcribe.slnx --filter-class "*.TrayIconServiceTests"` passes.

## 2. One save at a time

- [x] 2.1 Guard `JsonSettingsStore.SaveAsync` with a `SemaphoreSlim(1, 1)` around the write, the move and `Changed` (D5), and mention it in the `ISettingsStore.SaveAsync` docs. Verify: `dotnet build Pisum.Transcribe.slnx` passes.
- [x] 2.2 Add a `JsonSettingsStoreTests` test: two concurrent `SaveAsync` calls both succeed, `Current` is the settings of the one that finished last, and `Changed` is raised twice. Verify: `dotnet test Pisum.Transcribe.slnx --filter-class "*.JsonSettingsStoreTests"` passes.

## 3. The task switch in the tray menu

- [x] 3.1 Add to `DictationMessages`: `LanguageName(code)` (the culture's `EnglishName`, the code for an unknown culture), `TranscribeMenuItem(source)` ("Transcribe (German)"), `TranslateMenuItem(source, target)` ("Translate (German → English)"), and the failed switch's notification title and message (D4). Verify: `dotnet build Pisum.Transcribe.slnx` passes.
- [x] 3.2 Add `Dictation/TaskMenu.cs`, a hosted service that adds the two radio items on the UI thread in `StartAsync` and saves the chosen task with `SwitchAsync`, which does nothing for the task in effect, and logs a warning and notifies on a failed save (D2). Register it in `AddDictation()` before `DictationFeedback`. Verify: `dotnet build Pisum.Transcribe.slnx` passes.
- [x] 3.3 Add `tests/Pisum.Transcribe.Tests/Dictation/TaskMenuTests.cs` (`Unit`, with `InlineUiDispatcher` and a fake `ITrayIconService` that captures the items), for spec `dictation` "Task switch in the tray menu":
  - the headers and check states for `translate` `de`→`en` ("Menu shows the saved task")
  - choosing Transcribe saves the task `transcribe` with the languages unchanged ("Switching to transcribe")
  - choosing the checked item doesn't call `SaveAsync` ("Choosing the checked item")
  - a throwing `SaveAsync` shows the notification, and the check state still follows `Current` ("Save fails")
  - an unknown language code shows as the code, and `LanguageName` returns English names

  Verify: `dotnet test Pisum.Transcribe.slnx --filter-class "*.TaskMenuTests"` passes.
- [x] 3.4 Add a `SettingsViewModelTests` test: after a save of the other task through the store, `Dictation.Task` shows it and `HasChanges` is `false` ("Open settings window follows"). Verify: `dotnet test Pisum.Transcribe.slnx --filter-class "*.SettingsViewModelTests"` passes.
- [x] 3.5 Add a `DictationControllerTests` test, if the existing ones don't cover it already: a dictation started with `translate` transcribes with `translate` although the task is saved as `transcribe` during its transcription, and the next one uses `transcribe` ("Switching during a dictation"). Verify: `dotnet test Pisum.Transcribe.slnx --filter-class "*.DictationControllerTests"` passes.

## 4. The mode in the tooltip

- [x] 4.1 Add `DictationMessages.ModeLine(TranscriptionSettings)` ("Translate: German → English", "Transcribe: German") and let `ToolTip` take the mode line as a second line. In `DictationFeedback`, take `ISettingsStore`, subscribe to `Changed` in `StartAsync` and unsubscribe in `StopAsync`, render on the UI thread on a change, and build the tooltip from `Current` in `Render()` (D3). Update the class's remarks. Verify: `dotnet build Pisum.Transcribe.slnx` passes.
- [x] 4.2 Update `DictationFeedbackTests` for spec `dictation` "Task in the tray tooltip": the ready tooltip is "Pisum Transcribe – Ready (CPU)\nTranslate: German → English", a settings change during idle and during recording changes the second line, and tooltip assertions elsewhere that compare the whole text use the new format. Verify: `dotnet test Pisum.Transcribe.slnx --filter-class "*.DictationFeedbackTests"` passes, and so does the whole `dotnet test Pisum.Transcribe.slnx`.

## 5. Docs and wrap-up

- [x] 5.1 Update `docs/roadmap.md`: add GitHub #40 `add-tray-task-switch` to the table, the dependency graph and the status notes, as done for #39. Update the tray menu description in `CLAUDE.md`'s Architecture section to mention the optional check state of `AddMenuItem`. Verify: `grep -n "#40" docs/roadmap.md` shows the row and the note.
- [ ] 5.2 Check by hand on Windows: `dotnet run --project src/Pisum.Transcribe -f net10.0-windows10.0.19041.0`, open the tray menu. Verify: **Transcribe (German)** and **Translate (German → English)** show above **Settings…** with a radio mark on the task in effect, choosing the other one moves the mark, the tooltip's second line follows, and the next dictation uses the new task. (macOS: the same in the menu bar menu, checked by hand on a Mac; noted on the PR if not done.)
- [ ] 5.3 Final check. Verify: `openspec validate add-tray-task-switch --strict` passes, and `dotnet build Pisum.Transcribe.slnx` and `dotnet test Pisum.Transcribe.slnx` pass on Windows. CI passes on Windows and macOS on the PR, which references #40 without a closing keyword.
