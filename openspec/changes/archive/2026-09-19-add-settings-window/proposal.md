## Why

After `add-dictation-workflow`, every preference exists only as a value in `settings.json`: hotkey, task and language pair, model, backend and insertion method. Users cannot reasonably edit JSON to switch from German→English translation to plain German transcription, choose a smaller model for a weaker machine, force the CPU backend when Vulkan misbehaves, or pick a hotkey that doesn't clash with their shortcuts. `docs/idea.md` lists these as the app's settings (language pair, model and quant selection, paste method), and all of them take effect only after a restart today.

## What Changes

- Add a **Settings window**, opened from a new **Settings…** tray menu item or by double-clicking the tray icon. There is only one window instance.
- **Dictation section:** a hotkey editor ("press the new combination"), task (Translate / Transcribe), and source and target language pickers limited to what the selected model supports.
- **Model section:** the catalog models with installed state and size; download (with progress) and delete for models that are not active; the active model; the backend preference (Auto / Vulkan / CPU) with the backend currently in use; license attribution. Download and delete take effect immediately, outside Save and Close.
- **Text insertion section:** method (Paste via clipboard / Type text) and "Restore clipboard after paste".
- **General section:** "Start with Windows" (per-user autostart, default off). Its state is read from and written to Windows' own startup entry, not stored in `settings.json`, so the option also reflects a change made in Task Manager.
- **Save** applies the changes without restarting and keeps the window open: the new hotkey is active immediately, a model or backend change reloads the engine in the background, and language and insertion changes apply to the next dictation. **Close** discards unsaved changes. Save writes only the fields the user changed, so a model chosen meanwhile in the setup window is not overwritten.
- A dictation in progress keeps the task, language and insertion settings it started with. A model or backend change does not wait for it: a transcription already running completes on the previous model, but a recording still running is not transcribed if the reload has not finished when the hotkey is released. Saving a new hotkey cancels a dictation held with the old one.
- Validate before saving: the language pair must be supported by the model, the selected model must be installed, and the hotkey must include a modifier or function key.
- The engine reloads while running, and the most recently saved model and backend win. Models can be deleted, and a model downloads at most once at a time.

Differences from issue #7:
- The issue names the second button **Cancel**. Because Save keeps the window open, it is **Close** here.
- The issue says a dictation in progress completes with its original settings. That holds for task, language and insertion settings, and for a transcription already running during a model or backend change. It does not hold for a recording still running during a model or backend change, or held with the old hotkey (see above).

## Capabilities

### New Capabilities
- `settings-window`: The user interface for viewing and changing all user settings. Covers how it opens, its sections and controls, Save and Close, validation, model download and delete from settings, applying changes live, and the per-user autostart option.

### Modified Capabilities
- `transcription`: The engine reloads when the saved model or backend changes while it runs. The most recently saved model and backend win, and a model that is not installed is not loaded until it becomes installed.
- `model-management`: Installed models that are not selected can be deleted, and only one download of a model runs at a time.
- `push-to-talk-hotkey`: The hotkey can be changed while the application runs and is suspended while the user records a new one. Key privacy covers the keys held during that recording.

## Impact

- New feature folder `src/Pisum.Transcribe/SettingsWindow/` with `services.AddSettingsWindow()`: the window and its service, view models, `HotkeyRecorder`, `SettingsApplier` and `StartupRegistration`.
- Internal contract extensions:
  - `ISettingsStore.Changed` event
  - `IPushToTalkHotkey.SetHotkey` / `Suspend` / `Resume` and a raw key event for recording
  - `IModelStore.Delete`, and `IModelStore.InstallAsync` rejects a model that is already downloading
  - `ITranscriber.LoadAsync` is valid in every state, and the newest load wins
  - `ITrayIconService.DoubleClicked` event
- The engine's backend failure message points to the backend setting instead of only to a restart.
- Registry: writes and removes the value `Pisum Transcribe` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, and removes Task Manager's override under `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run` when autostart is turned on. No new settings section.
- No new packages. Reuses CommunityToolkit.Mvvm.
- Depends on all of the earlier changes, through `add-dictation-workflow`.
