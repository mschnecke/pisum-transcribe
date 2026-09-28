## Why

Switching between translating and transcribing is a common, quick decision: a German message to a colleague needs the German text, an English reply needs the translation. Today the switch sits in the settings window, behind **Settings…**, a section, a radio button and **Save**. It should be one click in the tray menu, and the user should see which mode is on without opening anything.

Tracked in issue #40. The decisions come from explore mode on 2026-09-28.

## What Changes

- **Two radio items in the tray menu.** The tray menu (the menu bar menu on macOS) shows **Transcribe (<source>)** and **Translate (<source> → <target>)**, such as **Transcribe (German)** and **Translate (German → English)**, with the item of the current task checked. The language names are the English names the settings window shows. The items sit above **Settings…** and are shown even while no model is installed.
- **Choosing an item saves the task.** Choosing the unchecked item saves `transcription.task` to `settings.json`; choosing the checked one does nothing. The next dictation uses the new task, a dictation in progress keeps the task it started with, and an open settings window shows the new task. A failed save shows a notification, and the check mark stays on the task in effect.
- **The tooltip names the mode.** The tray tooltip gets a second line with the current mode, such as `Translate: German → English` or `Transcribe: German`, in every tray state, and follows a change of the task or the languages at once.
- **Checked menu items in the tray service.** `ITrayIconService.AddMenuItem` takes an optional check state, which makes the item a radio item, read each time the menu opens.
- **Serialized saves.** `JsonSettingsStore.SaveAsync` saves one at a time, so a switch from the tray and a save from the settings window can't collide on the temporary file.

Not in scope: a hotkey or a modifier to switch the mode, a language picker in the tray menu, and a mode indicator in the recording overlay.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `dictation`: two new requirements, "Task switch in the tray menu" (the radio pair, saving, the failed save, a dictation in progress) and "Task in the tray tooltip" (the second line of the tooltip).

## Impact

- **Code:** `Tray/ITrayIconService.cs` and `Tray/TrayIconService.cs` (the optional check state, set in `UpdateMenuItems`). A new `Dictation/TaskMenu.cs`, a hosted service that adds the two items and saves the task, registered in `AddDictation()` before `DictationFeedback`. `Dictation/DictationFeedback.cs` reads the settings for the tooltip's second line and renders again on `ISettingsStore.Changed`. `Dictation/DictationMessages.cs` (the headers, the tooltip line, the failed save's notification). `Settings/JsonSettingsStore.cs` (one save at a time).
- **Tests:** `TrayIconServiceTests` (radio items and their check state on open), new `TaskMenuTests`, `DictationFeedbackTests` (the tooltip line and its update on a settings change), `JsonSettingsStoreTests` (concurrent saves).
- **Settings:** no new setting. `transcription.task` keeps its values, so the settings file and `AppSettings.CurrentSchemaVersion` stay unchanged, and `SettingsApplier` needs no case, because each dictation reads the task when it starts.
- **Platforms:** shared code only. Avalonia's `NativeMenuItem` radio state covers the Windows tray popup and the macOS native menu; no `Windows/` or `MacOS/` code and no Swift helper change.
- **Docs:** `docs/roadmap.md` (issue #40 and its change).
- **Issue:** the PR references #40 without a closing keyword; the issue is closed when the change is done.
