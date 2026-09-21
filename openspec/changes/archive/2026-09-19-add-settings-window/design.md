## Context

All settings sections already exist in `AppSettings`: `Model`, `Transcription`, `Recording`, `TextInsertion`, from the earlier changes. Their defaults and load-time validation are defined by those capabilities. Several consumers read settings only at startup or per dictation:
- `TranscriberHostedService` loads the selected model once at startup, and again when `IModelStore.ModelInstalled` reports the selected model.
- `SharpHookPushToTalkHotkey` builds its detector once.
- `DictationController` snapshots the transcription and text insertion settings at each press, which is already live.

`TranscribeCppTranscriber.LoadAsync` throws unless the status is `NotLoaded` or `Failed`. `ModelSetupViewModel` saves `SelectedModelId` before its download starts, as the model-management spec requires, so a model can be selected while it is not installed yet. It also implements download progress and cancel with CommunityToolkit.Mvvm.

## Goals / Non-Goals

**Goals:**
- One settings window covering every user-facing setting, with live apply on save.
- View models testable without WPF, reusing the model download logic instead of duplicating it.

**Non-Goals:**
- No import or export of settings, and no profiles or per-application settings.
- No UI localization. English only, as in `add-dictation-workflow`.
- No microphone device picker. It is still a non-goal from `add-push-to-talk-recording`.
- No toggle for voice activity detection here (see `add-voice-activity-detection`, which adds it to this window).
- No "unsaved changes" prompt on close. Closing discards.
- No background downloads that outlive their window, and no shared download progress between the setup and settings windows.
- No waiting for a running dictation to end before a reload or a hotkey change.

## Decisions

### D1: Draft-and-commit editing, Save keeps the window open

`SettingsViewModel` loads a copy of `ISettingsStore.Current` into section view models:
- `DictationSectionViewModel`
- `ModelSectionViewModel`
- `TextInsertionSectionViewModel`
- `GeneralSectionViewModel`

The draft keeps the *baseline* it was loaded from and tracks which fields differ from it. The dictation section validates the languages with `TranscriptionOptionsValidator` and shows the result in two properties, `SourceLanguageError` and `TargetLanguageError`. The source is checked alone first, so an error names the setting that the model does not support. `SaveCommand.CanExecute` = no language error and at least one changed field.

*Why plain error properties over `ObservableValidator`:* the only rule spans two sections, the model in one and the languages in the other, and must run again when either changes. Attribute validation would need a custom attribute that reads the other section, for no gain.

- **Save** builds a new immutable `AppSettings` from the draft with `with` expressions and calls `SaveAsync`, but only when a settings field changed. It then calls `IStartupRegistration.SetEnabled` if the autostart checkbox changed (D7). After a successful save the draft rebases on the saved values, so Save is disabled until the next edit. The window stays open. A failed save or registry write shows an error in the window and leaves the draft dirty. A registry failure does not undo the settings that were already saved.
- **Settings changed elsewhere:** the view model subscribes to `ISettingsStore.Changed`. A save from elsewhere, today only the setup window's model choice, updates the baseline and every field the user has not edited. Fields the user edited keep their draft value. Validation then runs on the result, so Save writes exactly what the window shows and never writes back a stale value for a field the user did not touch.
- **Close** (the button, the title bar or Alt+F4) discards the draft without asking. While a download started in this window runs, closing asks first, as the setup window does, unless the application is stopping.

The window is a single `SettingsDialog` with a left navigation list and a content area per section, not a `TabControl`, so the next change can add a section cheaply. It is not called `SettingsWindow`, because a type named like its namespace `Pisum.Transcribe.SettingsWindow` makes references to either ambiguous. `SettingsWindowService` keeps a single dialog instance and activates it if it is already open. It adds the tray item through `ITrayIconService.AddMenuItem("Settings…")` and subscribes to the new `ITrayIconService.DoubleClicked` event, which `TrayIconService` raises from H.NotifyIcon's `TrayMouseDoubleClick` on the UI thread.

All new code lives in the feature folder `SettingsWindow/` (namespace `Pisum.Transcribe.SettingsWindow`), registered by `services.AddSettingsWindow()` after `AddDictation()`. The `Settings` folder stays the storage feature that every other feature depends on; the window depends on Recording, SpeechModels and Transcription, so putting it there would make storage depend on its consumers.

### D2: `ISettingsStore.Changed` and `SettingsApplier`

`ISettingsStore` gains `event EventHandler<SettingsChangedEventArgs>? Changed`, with `Previous` and `Current`, raised after a successful save on the thread that saved. `SettingsApplier : IHostedService` compares sections and reacts only to differences:

| Difference | Action |
|---|---|
| `Recording.Hotkey` | `IPushToTalkHotkey.SetHotkey(keys)`, which rebuilds the detector (resetting it with Cancelled if active) |
| `Model.SelectedModelId` or `Transcription.Backend`, and the selected model is installed | `ITranscriber.LoadAsync(selected model, backend)`, not awaited |
| `Model.SelectedModelId` or `Transcription.Backend`, and the selected model is not installed | nothing; `TranscriberHostedService` loads it when `ModelInstalled` reports it, with the backend saved at that time |
| `Transcription` task and languages, `TextInsertion` | nothing; they are read per dictation |

Autostart is not part of `AppSettings` (D7), so it has no row here.

*Why the installed check:* the setup window saves its model choice before downloading. Without the check, that save would load a missing file, fail, and show "Model failed to load" during a normal first-run download. Until the new model is installed, the engine keeps whatever it has loaded.

*Why a separate applier over consumers subscribing directly:* the "what reacts to what" table lives in one tested place, and component services stay unaware of the settings UI.

### D3: Engine reload

`ITranscriber.LoadAsync` becomes valid in every state and no longer throws `InvalidOperationException`. It sets the status to `Loading` before it returns, as today, so new requests are rejected as "loading" from that moment. On the worker, each load:
1. Runs after the requests queued before it. They are already on the worker, so FIFO order lets them finish on the previous model. The worker therefore keeps its own loaded model and backend until it disposes them. The public `ActiveBackend` still reads `null` while the status is not `Ready`.
2. Disposes the session and model.
3. Loads the new model with fallback and warm-up, as before.

**The newest load wins.** Each `LoadAsync` call takes a generation number. The worker skips a queued load when a newer one is queued. A load that is already running cannot be interrupted, because the native call cannot be cancelled safely. When it finishes and a newer load is waiting, the worker disposes the result without reporting `Ready` and continues. The status therefore stays `Loading` until the most recently requested model and backend are loaded or have failed. A skipped or superseded call's task completes normally. `TranscriberHostedService` no longer needs its `InvalidOperationException` catch.

**A dictation in progress (see the settings-window spec):**
- A transcription already running or queued completes on the previous model (step 1).
- A recording still running when the reload starts reaches `TranscribeAsync` after the status became `Loading`. It is rejected with `TranscriberNotReadyException` ("The model is still loading."), which `DictationController` already shows as a "Dictation failed" notification. If the reload has finished by the time the hotkey is released, the recording is transcribed on the new model with the settings it started with. The controller needs no change.

**Failure message:** `BackendFailedMessage` becomes "The speech model stopped working. Change the backend in Settings, or restart Pisum Transcribe to load it again. Details are in the log." A backend change is a settings difference, so it reloads from `Failed`. Saving unchanged settings does not.

### D4: Hotkey recording

`IPushToTalkHotkey` gains `Suspend()`, `Resume()` and `event EventHandler<RawKeyEventArgs>? RawKey`, which is raised only while suspended. `Suspend` resets the detector, raising Cancelled if the hotkey was active. `HotkeyRecorder` (pure logic, unit-tested) accumulates the maximum set of simultaneously held keys, completes when the held set becomes empty, and cancels on Esc or on `Cancel()`. Validation: the set must contain a modifier (`VcLeft/RightControl`, `VcLeft/RightAlt`, `VcLeft/RightShift`, `VcLeft/RightMeta`) or `VcF1`–`VcF24`.

The recorder stores the exact keys, because `PushToTalkDetector` treats left and right keys as different keys. The saved value is the list of `KeyCode` names, which `HotkeyParser` reads back. The UI shows names through a small `KeyCode` → display-name map that always names the side of a two-sided key: modifiers first in the order Ctrl, Alt, Shift, Win, then the other keys, joined with "+", such as "Right Ctrl" or "Left Ctrl+Left Win". Other keys show their plain name, such as "F13" or "A".

`DictationSectionViewModel` runs the recording. Its **Change…** command subscribes to `RawKey` and then calls `Suspend`. Every way the recording ends unsubscribes and calls `Resume`: a captured or rejected hotkey, Esc, and `CancelHotkeyRecording()`. `SettingsDialog` calls `CancelHotkeyRecording()` when it is deactivated, and `SettingsViewModel.OnClosed()` calls it when the dialog closes. Ending capture on deactivation keeps keys typed in other applications out of the recorder, as the push-to-talk-hotkey privacy requirement requires. Raw keys are never logged.

*Why in the view model instead of the dialog:* every path that must resume push-to-talk can be unit-tested there, including closing the dialog during a recording.

*Why SharpHook raw events instead of WPF `KeyDown`:* they are identical key codes to the detector, so what you record is exactly what triggers. WPF also cannot see the Win key reliably, because the Start menu intercepts it.

### D5: Languages

`LanguageOptions.For(SpeechModel model, TranscriptionTask task, string source)` returns the source options (the model's languages) and the target options, per the spec rules. Display names come from `CultureInfo.GetCultureInfo(code).EnglishName` (e.g. "German"), sorted by name. Validation reuses `TranscriptionOptionsValidator` from `add-transcription-engine`, so the UI and engine rules cannot drift apart.

### D6: Model section

Each catalog entry gets a `ModelItemViewModel` (installed, active, `DownloadCommand`, `CancelCommand`, `DeleteCommand`, progress, error). The download and progress logic is extracted from `ModelSetupViewModel` into a shared `ModelDownloadViewModel`, which the setup window and the settings window both use.

- **Downloads belong to their window.** A download runs while the window that started it is open. Closing the window during a download asks first and then cancels it, as the setup window does.
- **One download per model:** `ModelStore` tracks the models being downloaded. `InstallAsync` for a model that is already downloading throws a new `ModelDownloadInProgressException` without touching the running download or its `.partial` file. Both windows show "This model is already downloading." Without the check, the second download would fail on the locked `.partial` file with an unclear error.
- **Delete:** `IModelStore.Delete(model)` removes the file. It throws if asked to delete the model that is currently selected in the *saved* settings. File errors such as a file that is still open propagate. The UI hides Delete for the saved and the draft-selected model, disables it while `ITranscriber.Status` is `Loading`, asks for confirmation through a `MessageBox`, and shows a failed delete in the model's row. The model then stays installed. Disabling Delete while loading covers deleting the previous model right after a switch, while the worker may still hold it.
- **Immediate actions:** Download and Delete change the models folder at once. They are not part of the draft, so Save does not apply them and Close does not undo them. Installed states refresh on `ModelInstalled`, after a delete, and when the window is activated, which also picks up a file the engine deleted as damaged.
- **Selecting a not-installed model:** not allowed. The radio button is disabled until the model is installed.
- **Status line:** the backend in use and engine status come from `ITranscriber.Status`, `ActiveBackend` and `StatusChanged`. Because the window stays open after Save, the line shows a reload as it happens.

### D7: Start with Windows

The registry is the only store; `AppSettings` gets no `General` section. `IStartupRegistration` (behind a small registry seam for tests):
- `IsEnabled`: the value `Pisum Transcribe` exists under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, and Task Manager has not disabled it. Task Manager's startup apps page disables an entry with a binary value of the same name under `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run`, whose first byte is odd when disabled (`0x03`) and even when enabled (`0x02`). A missing or unreadable value counts as enabled.
- `SetEnabled(true)`: writes the Run value as `"<Environment.ProcessPath>"` and deletes the `StartupApproved` value, which restores the default of enabled.
- `SetEnabled(false)`: deletes the Run value and the `StartupApproved` value.
- At app startup (`IHostedService`), if the Run value exists and differs from the current quoted `ProcessPath`, for example after the app moved, it is rewritten. If it does not exist, the registry is not touched.

The window reads `IsEnabled` when it opens and after each save.

*Why the registry only:* a copy in `settings.json` goes stale as soon as the user changes the entry in Task Manager, and startup re-sync must not override that choice. Reading the registry makes the checkbox show what Windows will do at sign-in.

*Alternative:* a shortcut in the Startup folder. The Run key is simpler and needs no COM `IShellLink`. The packaging change (Velopack) may later replace the path handling.

## Risks / Trade-offs

- [A model reload drops the ability to dictate for several seconds, and 1.1 GB models briefly double memory if the old one is not freed first.] → D3 disposes before loading. The status line and tray show "Loading model…".
- [Superseded loads still run to completion when they have already started, so two quick saves can cost two loads.] → Accepted. Only a load that is already running is finished; queued ones are skipped.
- [A recording running while a model or backend change is saved is lost if the reload is still running at release.] → Accepted and specified. It needs the user to hold the hotkey while clicking Save, and the notification explains it.
- [A hotkey change while the user holds the old hotkey.] → `SetHotkey` resets the detector with Cancelled, so the controller aborts cleanly.
- [Suspend must always be followed by Resume, or push-to-talk stays dead.] → Every end of a recording resumes in `DictationSectionViewModel`, and the dialog cancels the recording on `Deactivated` and `Closed`. A unit test covers close-while-capturing.
- [The `StartupApproved` format is undocumented and may change.] → Only the first byte is read, anything unreadable counts as enabled, and the manual check in task 4.2 confirms the format on the target Windows build.
- [The Run-key path breaks if the user moves the exe while autostart is on and does not start the app manually.] → Accepted. Startup re-sync fixes it on the next manual start. Velopack packaging will revisit it.
- [Two windows can show the same model with different states for a moment, for example one downloading and the other offering Download.] → The second start is rejected with a clear message (D6), and installed states refresh on `ModelInstalled`.
- [Language names from `CultureInfo` depend on ICU / NLS data.] → Invariant-globalization mode is not enabled, and all 25 codes are standard neutral cultures.
