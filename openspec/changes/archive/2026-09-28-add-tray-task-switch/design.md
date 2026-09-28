## Context

See proposal.md for the motivation and specs/dictation/spec.md for the behavior.

**Current state:**
- `transcription.task` (`Translate` by default) with `sourceLanguage` and `targetLanguage` already exists in `TranscriptionSettings`. `DictationController` reads it from `ISettingsStore.Current` when each dictation starts (`DictationController.cs:184`), so a saved task needs no live apply, and `SettingsApplier` has no case for it.
- `SettingsViewModel` subscribes to `ISettingsStore.Changed` and rebases its sections on the new settings (`ApplyBaseline`), so an open settings window already follows a save made somewhere else.
- `TranscriptionOptionsValidator` accepts any source the model knows for `Transcribe`, and the settings window only saves a valid `Translate` pair. Switching the task alone therefore leaves the settings valid.
- `ITrayIconService.AddMenuItem(header, onClick, isVisible)` adds plain items above the separator before **Exit**, in the order they are added. `TrayIconService.UpdateMenuItems` reads the header and visibility of every item each time the menu opens (the Win32 popup's `WindowOpened` class handler on Windows, `NativeMenu.Opening` on macOS).
- Items appear in registration order: `AddSpeechModels` (the setup item), `AddDictation` (**Cancel transcription**, added by `DictationFeedback.StartAsync`), `AddSettingsWindow` (**Settings…**), `AddUpdates`.
- `DictationFeedback` is the only writer of the tray icon and tooltip. `Render()` combines the phase, the engine status and the hotkey availability into `DictationMessages.ToolTip(state)`, "Pisum Transcribe – <state>".
- `JsonSettingsStore.SaveAsync` writes `settings.json.tmp` with `FileShare.None` and moves it over the file, with no lock. Today only the settings window saves, one save at a time; the tray switch adds a second writer.
- `LanguageOptions` (in `SettingsWindow/`) names a language with `CultureInfo.GetCultureInfo(code).EnglishName`, which throws `CultureNotFoundException` for an unknown code.

## Goals / Non-Goals

**Goals:**
- The tray menu and the settings window edit the same setting, through the same store, so there is only one source of truth.
- The tray service learns a check state in a way any feature can use, without knowing about tasks.

**Non-Goals:**
- Validating the languages again when switching. The settings window keeps being the place that makes a pair valid; a pair a hand-edited file made invalid fails at the next dictation as it does today.
- A separator around the two items. `ITrayIconService` has none for features, and two items don't need one.

## Decisions

### D1: `AddMenuItem` takes an optional `Func<bool>? isChecked`

```
AddMenuItem(Func<string> header, Action onClick, Func<bool>? isVisible = null, Func<bool>? isChecked = null)
                                                                               |
       isChecked != null --> NativeMenuItem.ToggleType = Radio                 |
       UpdateMenuItems (menu opens) --> item.IsChecked = isChecked() --------+
```

Both overloads get the parameter. An item with `isChecked` is a radio item; `UpdateMenuItems` sets `IsChecked` for shown items as it sets `Header`. The service stores it in the `_menuItems` tuple.

*Alternatives:* a separate `AddRadioGroup(...)` API. It would model the group explicitly, but Avalonia's `NativeMenuItem` has no group; mutual exclusion comes from the callers' `isChecked` returning one `true`, so a group API adds a type and nothing the callers need. A checkbox item ("Translate to English") was rejected in explore mode (option B).

Checked in Avalonia 12.1.1 (decompiled): the Win32 tray popup (`TrayIconImpl`'s `TrayPopupRoot`) builds its items with `NativeMenuBarPresenter.CreateContainerForNativeItem`, which binds the `MenuItem`'s `ToggleType` one way and `IsChecked` two way to the `NativeMenuItem`. A click on a radio `MenuItem` checks it and unchecks its siblings through `DefaultMenuInteractionHandler`, and the two-way binding writes that back to the `NativeMenuItem`. That's harmless: the next open sets `IsChecked` from the settings again, so a failed save shows the task in effect (spec "Save fails"). On macOS the native menu exporter maps the same two properties to the `NSMenuItem`.

### D2: A new `Dictation/TaskMenu` hosted service owns the items

`internal sealed class TaskMenu : IHostedService` takes `ITrayIconService`, `ISettingsStore`, `INotifier`, `IUiDispatcher`, `ILogger<TaskMenu>` and `IHostApplicationLifetime` (for `ApplicationStopping` as the save's token). `StartAsync` adds the two items on the UI thread:

```
header:    () => DictationMessages.TranscribeMenuItem(names of Current.Transcription)
isChecked: () => Current.Transcription.Task == Transcribe
onClick:   () => _ = SwitchAsync(TranscriptionTask.Transcribe)
(same for Translate)

SwitchAsync(task):
  current = store.Current
  if current.Transcription.Task == task: return          // spec "Choosing the checked item"
  try   await store.SaveAsync(current with { Transcription = current.Transcription with { Task = task } }, stopping)
  catch (Exception e) when not OperationCanceledException from stopping:
        log warning (status only), notifier.Show(DictationMessages.TaskSwitchFailedTitle, ...Message)
```

`AddDictation()` registers it before `DictationFeedback`, so its items come first in the menu (above **Cancel transcription**, which is only visible while transcribing), and above **Settings…**.

*Alternatives:* adding the items in `DictationFeedback`, which already has the tray and becomes a settings reader for the tooltip (D3). It is already the busiest class of the feature, and the menu's saving and error handling are unrelated to rendering. A `Transcription/` home was also considered, but the items are about how dictation behaves, and the `dictation` spec owns the tray's states.

### D3: `DictationFeedback` renders the tooltip's second line

`DictationFeedback` takes `ISettingsStore`, subscribes to `Changed` in `StartAsync` (unsubscribes in `StopAsync`), and on a change marshals to the UI thread and calls `Render()`. `Render()` builds `DictationMessages.ToolTip(state, mode)`, "Pisum Transcribe – <state>\n<mode>", where `mode` is `DictationMessages.ModeLine(transcription settings)`.

It stays the only writer of the tooltip (its remarks say so), and the second line is appended in every phase, so no state rule of "Tray icon states" changes. `Changed` is raised on the saving thread; reading `Current` on the UI thread in `Render()` gives the newest settings even if two changes arrive together.

A newline works in both tooltips: the Windows notification-area tooltip and the macOS status item's tooltip show multi-line text. The longest tooltip, such as "Pisum Transcribe – Accessibility access needed for the hotkey" plus "Translate: Portuguese → English", stays below the 127 characters of the Windows tooltip.

*Alternative:* the mode in parentheses on the same line, "Ready (CPU, Translate de→en)". It mixes the engine status with a setting, doesn't fit the non-ready states, and codes are harder to read than names.

### D4: Language names in `DictationMessages`, with a fallback to the code

`DictationMessages.LanguageName(code)` returns `CultureInfo.GetCultureInfo(code).EnglishName`, the same name `LanguageOptions` shows, and the code itself when the culture is unknown (catching `CultureNotFoundException`), so a hand-edited file can't break the menu or the tooltip.

*Alternative:* calling `LanguageOptions` from `Dictation/`. It computes whole option lists per model and would make `Dictation` depend on the settings window's feature for one name. A one-line helper reads the same `EnglishName` without that dependency.

### D5: `JsonSettingsStore` saves one at a time

`SaveAsync` waits on a `SemaphoreSlim(1, 1)` around the write, the move and the `Changed` event. A tray switch during a save from the settings window then waits instead of failing on the locked `settings.json.tmp`. Each caller builds its settings from `Current` or its rebased baseline, so the later save wins with the other's change included, except for the settings window's unsaved edits, which it rebases (existing behavior).

*Alternative:* ignoring the collision and relying on the failed-save notification. It's rare, but a user who clicks the tray while the settings window saves would see a misleading error for a correct action.

## Risks / Trade-offs

- [The radio mark's look in the Win32 popup and the macOS menu isn't covered by a test] → The binding is confirmed in Avalonia 12.1.1 (D1), and the Fluent theme styles `MenuItem:radio`; a `TrayIconServiceTests` test checks the item's state, and task 5.2 checks both platforms by hand before the PR. An Avalonia upgrade that changes the tray popup is caught by that test only for the state, not the look.
- [The check mark can be stale while the menu is open, if the settings window saves at that moment] → The next open reads the settings again; a click on the stale item saves the task it names, which is what the user chose.
- [Two lines in the tooltip change every existing tooltip assertion that compares the whole text] → Tests that use `ShouldBe` on the tooltip move to the new format or to `ShouldContain`; the spec only requires "contains".
- [`SaveAsync` holding the semaphore while `Changed` handlers run] → The handlers only queue work on the UI thread (`InvokeAsync`) and return, so they can't wait on another save.

## Migration Plan

None. No setting is added or renamed, so the settings format stays at `AppSettings.CurrentSchemaVersion` 2, and a rollback reads the same file.
