# settings-window Specification

## Purpose

Defines the window in which users view and change Pisum Transcribe's settings, and how saved changes take effect while the application keeps running.

## Requirements

### Requirement: Opening the settings window
The tray menu SHALL contain a **Settings…** item. On Windows, a left click on the tray icon SHALL also open the settings window, and a right click on the tray icon SHALL open the tray menu and SHALL NOT open the settings window. On macOS, a click on the menu bar icon SHALL open the menu and SHALL NOT open the settings window, so **Settings…** is the only way to open it. If the window is already open, it SHALL be brought to the foreground instead of opening a second one.

#### Scenario: Open from tray menu
- **WHEN** the user chooses **Settings…** in the tray menu
- **THEN** the settings window opens showing the currently saved settings

#### Scenario: Open with a click on the tray icon
- **WHEN** the settings window is closed and the user left-clicks the tray icon on Windows
- **THEN** the settings window opens showing the currently saved settings

#### Scenario: Right click opens the menu
- **WHEN** the user right-clicks the tray icon on Windows
- **THEN** the tray menu opens
- **AND** the settings window doesn't open

#### Scenario: Click on the menu bar icon on macOS
- **WHEN** the user clicks the menu bar icon on macOS
- **THEN** the menu opens
- **AND** the settings window doesn't open

#### Scenario: Already open
- **WHEN** the settings window is open and the user left-clicks the tray icon on Windows, or chooses **Settings…**
- **THEN** the existing window is activated
- **AND** no second settings window opens

#### Scenario: Double-click on the tray icon
- **WHEN** the settings window is closed and the user double-clicks the tray icon on Windows
- **THEN** one settings window is open and in the foreground

### Requirement: Save and close
The settings window SHALL offer **Save** and **Close**. Edits SHALL take effect only when the user chooses **Save**. **Save** SHALL apply the edits and keep the window open. **Save** SHALL be disabled while there are no unsaved edits and while any validation error is shown. **Close**, or closing the window in any other way, SHALL discard unsaved edits without asking. While a model download started in the settings window is running, closing SHALL first ask for confirmation that closing cancels the download: if the user confirms, the download SHALL be cancelled and the window SHALL close; if the user declines, the window SHALL stay open and the download SHALL continue. When the application exits, the window SHALL close without asking.

#### Scenario: Close discards edits
- **WHEN** the user changes the task to Transcribe and chooses **Close**
- **THEN** the saved task remains Translate

#### Scenario: Save keeps the window open
- **WHEN** the user changes the task to Transcribe and chooses **Save**
- **THEN** the saved task is Transcribe
- **AND** the settings window stays open
- **AND** **Save** is disabled until the next edit

#### Scenario: Closing during a download
- **WHEN** a download started in the settings window is running and the user chooses **Close** and confirms
- **THEN** the download stops within 2 seconds
- **AND** the settings window closes

### Requirement: Settings changed elsewhere
While the settings window is open, settings saved elsewhere, such as the model chosen in the setup window, SHALL be shown in every field the user has not edited. Saving SHALL NOT overwrite such a setting with the value the window showed before.

#### Scenario: Setup window selects a model while the settings window is open
- **WHEN** the settings window is open, the user has changed only the hotkey, and the setup window saves `canary-180m-flash-q8_0` as the selected model
- **THEN** the settings window shows `canary-180m-flash-q8_0` as the active model
- **AND** after the user chooses **Save**, the selected model is still `canary-180m-flash-q8_0`

### Requirement: Dictation settings
The dictation section SHALL let the user set the task (Translate or Transcribe), the source language and, for Translate, the target language. Language pickers SHALL show language names and SHALL offer only languages the selected model supports. For Translate, the target options SHALL be English when the source is not English, and the model's non-English languages when the source is English. For Transcribe, the target picker SHALL be hidden.

#### Scenario: Translate from German
- **WHEN** the task is Translate and the source language is German
- **THEN** the target language picker offers only English

#### Scenario: Small model limits languages
- **WHEN** the selected model is `canary-180m-flash-q8_0`
- **THEN** the source language picker offers exactly English, German, French and Spanish

### Requirement: Language validation on model change
If a different model is chosen and the current language settings are not supported by it, the settings window SHALL show a validation error on the language setting until a supported language is chosen.

#### Scenario: Switching to small model with Polish source
- **WHEN** the source language is Polish and the user selects `canary-180m-flash-q8_0`
- **THEN** the source language shows a validation error
- **AND** **Save** is disabled

### Requirement: Hotkey editor
The dictation section SHALL show the current push-to-talk hotkey and offer **Change…**. While changing, push-to-talk dictation SHALL be suspended, and the combination of keys the user holds together SHALL be captured when all of them are released. **Esc** SHALL cancel the change, and so SHALL the settings window losing focus. Because the hotkey distinguishes the left and right key of a pair, a key that exists on both sides of the keyboard SHALL be shown with its side.

The key names and the rule for a valid hotkey SHALL follow the platform:
- On Windows, a captured hotkey SHALL be rejected with a validation message unless it contains at least one of Ctrl, Alt, Shift, the Windows key, or a function key F1–F24. Keys SHALL be shown with Windows names, such as "Right Ctrl" or "Left Win", modifiers first in the order Ctrl, Alt, Shift, Win.
- On macOS, a captured hotkey SHALL be rejected with a validation message unless it contains at least one of Control, Option, Shift, Command, or a function key F1–F24. Keys SHALL be shown with Mac names, such as "Right Command" or "Left Option", modifiers first in the order Control, Option, Shift, Command. The fn/Globe key SHALL NOT be accepted, because the keyboard hook can't see it held.

#### Scenario: Record left Ctrl and left Win
- **WHEN** the user chooses **Change…** on Windows, holds the left Ctrl and left Win keys together and releases them
- **THEN** the hotkey field shows "Left Ctrl+Left Win"

#### Scenario: Record right Command on macOS
- **WHEN** the user chooses **Change…** on macOS, holds the right Command key and releases it
- **THEN** the hotkey field shows "Right Command"

#### Scenario: Reject fn on macOS
- **WHEN** the user chooses **Change…** on macOS, presses the fn key alone and releases it
- **THEN** a validation message says the hotkey must include Control, Option, Shift, Command or a function key F1–F24
- **AND** the previous hotkey is kept

#### Scenario: Reject a letter key
- **WHEN** the user chooses **Change…** and presses and releases the A key alone
- **THEN** a validation message says the hotkey must include a modifier or function key, named as the platform names them
- **AND** the previous hotkey is kept

#### Scenario: No dictation while recording a hotkey
- **WHEN** the hotkey editor is capturing and the user presses the current push-to-talk hotkey
- **THEN** no dictation recording starts

#### Scenario: Focus lost while recording a hotkey
- **WHEN** the user chooses **Change…** and switches to another application before pressing any key
- **THEN** the change is cancelled and the previous hotkey is kept
- **AND** pressing the current push-to-talk hotkey starts a dictation again

### Requirement: Model settings
The model section SHALL list all catalog models with name, download size, supported languages, installed state and license attribution, and SHALL mark the active model. An installed model SHALL be selectable as the active model; a model that is not installed SHALL NOT be selectable. A model that is not installed SHALL offer **Download** with progress and cancellation. An installed model that is neither the saved active model nor the model selected in the window SHALL offer **Delete**, which asks for confirmation. **Delete** SHALL NOT be available while the engine status is `Loading`. **Download** and **Delete** SHALL take effect immediately: they SHALL NOT need **Save**, and **Close** SHALL NOT undo them. When a download is rejected because the model is already downloading, or a delete fails, the model section SHALL say so.

#### Scenario: Download a second model
- **WHEN** the user chooses **Download** for `canary-180m-flash-q8_0`
- **THEN** download progress is shown in the model list
- **AND** when complete, the model is shown as installed and becomes selectable

#### Scenario: Delete an inactive model
- **WHEN** the user chooses **Delete** for an installed model that is not active and confirms
- **THEN** the model file is removed
- **AND** the model is shown as not installed

#### Scenario: Delete is not undone by Close
- **WHEN** the user deletes an installed model that is not active and then chooses **Close**
- **THEN** the model stays deleted

#### Scenario: Active model protected
- **WHEN** a model is the saved active model or the model selected in the window
- **THEN** no **Delete** action is available for it

#### Scenario: No delete while the engine loads
- **WHEN** the engine status is `Loading`
- **THEN** no **Delete** action is available for any model

#### Scenario: Model already downloading in the setup window
- **WHEN** the setup window is downloading `canary-180m-flash-q8_0` and the user chooses **Download** for it in the settings window
- **THEN** the settings window says that the model is already downloading
- **AND** the setup window's download continues

### Requirement: Backend settings
The engine section SHALL let the user choose the backend preference Auto, the GPU or CPU, and SHALL show the backend currently in use or the engine status if it is not ready. The GPU option SHALL be named for the platform's GPU backend: Vulkan (GPU) on Windows and Metal (GPU) on macOS.

#### Scenario: Current backend displayed
- **WHEN** the engine is ready on the GPU backend
- **THEN** the engine section shows that the GPU backend is in use, by its name

#### Scenario: GPU option on Windows
- **WHEN** on Windows the user opens the engine section
- **THEN** the GPU option is named Vulkan (GPU)

#### Scenario: GPU option on macOS
- **WHEN** on macOS the user opens the engine section
- **THEN** the GPU option is named Metal (GPU)
- **AND** when the engine is ready on the GPU backend, the section shows that Metal is in use

### Requirement: Text insertion settings
The text insertion section SHALL let the user choose between "Paste via clipboard" and "Type text", and toggle "Restore clipboard after paste". The restore option SHALL be disabled when "Type text" is chosen.

#### Scenario: Switch to typing
- **WHEN** the user selects "Type text" and saves
- **THEN** the next dictation is inserted by typing

### Requirement: Start with Windows
On Windows, the general section SHALL offer "Start with Windows", which is off until the user turns it on. When saved as on, Pisum Transcribe SHALL start automatically when the user signs in to Windows, also when its startup entry was disabled in Task Manager before. When saved as off, it SHALL no longer start automatically. The option SHALL show whether Windows will start Pisum Transcribe at sign-in, including after a change in Task Manager, and SHALL NOT be stored in the settings file. The option SHALL affect only the current Windows user and SHALL NOT require administrator rights.

On macOS, the general section SHALL offer "Open at login" instead, which is off until the user turns it on. When saved as on, Pisum Transcribe SHALL open when the user logs in to macOS. When saved as off, it SHALL no longer open at login. The option SHALL show whether macOS will open Pisum Transcribe at login, including after a change in *System Settings → General → Login Items*, and SHALL NOT be stored in the settings file. When macOS needs the user's approval before it opens Pisum Transcribe at login, for example because the user turned it off in Login Items, the option SHALL be shown as off, and a hint SHALL say to allow Pisum Transcribe in *System Settings → General → Login Items*. The option SHALL affect only the current macOS user and SHALL NOT require an administrator's password.

#### Scenario: Enable autostart
- **WHEN** the user enables "Start with Windows", saves, and signs out and in again
- **THEN** Pisum Transcribe is running with its tray icon

#### Scenario: Disable autostart
- **WHEN** the user disables "Start with Windows", saves, and signs out and in again
- **THEN** Pisum Transcribe is not started automatically

#### Scenario: Disabled in Task Manager
- **WHEN** "Start with Windows" is on and the user disables Pisum Transcribe on Task Manager's startup apps page
- **THEN** the next time the settings window opens, "Start with Windows" is off

#### Scenario: Enabled again after Task Manager
- **WHEN** Pisum Transcribe was disabled in Task Manager, and the user enables "Start with Windows", saves, and signs out and in again
- **THEN** Pisum Transcribe is running with its tray icon

#### Scenario: Enable open at login on macOS
- **WHEN** the user enables "Open at login" on macOS, saves, and logs out and in again
- **THEN** Pisum Transcribe is running with its menu bar icon
- **AND** *System Settings → General → Login Items* lists Pisum Transcribe

#### Scenario: Disable open at login on macOS
- **WHEN** the user disables "Open at login" on macOS, saves, and logs out and in again
- **THEN** Pisum Transcribe isn't opened automatically

#### Scenario: Turned off in Login Items on macOS
- **WHEN** "Open at login" is on and the user turns Pisum Transcribe off in *System Settings → General → Login Items*
- **THEN** the next time the settings window opens, "Open at login" is off, and the hint says to allow Pisum Transcribe in Login Items

#### Scenario: No autostart option on macOS
- **WHEN** the user opens the settings window on macOS
- **THEN** the general section shows "Open at login" and doesn't show "Start with Windows"

### Requirement: Check for updates automatically
The general section SHALL offer "Check for updates automatically", which is on until the user turns it off. The option SHALL be stored in the settings file, and a settings file without it SHALL load it as on. A hint under the option SHALL say that the application asks GitHub once a day whether a new version exists and sends nothing else. The option SHALL control the update check that the app-updates spec describes.

#### Scenario: On by default
- **WHEN** the application starts without a settings file and the user opens the settings window
- **THEN** "Check for updates automatically" is on

#### Scenario: Turned off and saved
- **WHEN** the user turns off "Check for updates automatically" and saves
- **THEN** the settings file stores the option as off
- **AND** after a restart, the option is still off and no check runs

### Requirement: Apply changes without restart
Saved changes SHALL take effect without restarting the application:
- A new hotkey SHALL be active immediately after saving. If the previous hotkey is held when saving, that dictation SHALL be cancelled and nothing SHALL be inserted.
- A change of active model or backend preference SHALL reload the engine in the background. The engine status SHALL be `Loading` during the reload, and dictation SHALL be unavailable until it is `Ready`.
- Task, language and text insertion changes SHALL apply to the next dictation. A dictation in progress SHALL keep the task, language and text insertion settings it started with.
- A dictation that is already being transcribed when a model or backend change is saved SHALL complete on the previous model before the reload starts.
- A recording that is still running when a model or backend change is saved SHALL be transcribed only if the reload has finished when the hotkey is released. Otherwise it SHALL NOT be transcribed, and the user SHALL be notified that the model is still loading.
- Turning "Check for updates automatically" off SHALL stop further update checks and hide an update notice that is shown. Turning it on SHALL start an update check within 1 minute.

#### Scenario: New hotkey without restart
- **WHEN** the user changes the hotkey from right Ctrl to left Ctrl+left Win and saves
- **THEN** holding left Ctrl+left Win starts a dictation
- **AND** holding right Ctrl alone no longer does

#### Scenario: Backend switch reloads engine
- **WHEN** the engine is ready on the GPU backend and the user saves backend CPU
- **THEN** the engine status becomes `Loading` and then `Ready` on CPU
- **AND** the open settings window shows the status change
- **AND** the application did not restart

#### Scenario: Language change applies to next dictation
- **WHEN** the user saves task Transcribe with source German
- **THEN** the next dictation inserts German text

#### Scenario: Transcription during a model change
- **WHEN** a dictation is being transcribed and the user saves a different installed model
- **THEN** the dictation's text is inserted
- **AND** the engine then reloads with the new model

#### Scenario: Recording during a model change
- **WHEN** the user holds the hotkey, saves a different installed model, and releases the hotkey while the reload is still running
- **THEN** nothing is inserted
- **AND** a notification says the model is still loading

#### Scenario: Old hotkey held while saving a new one
- **WHEN** a recording is running because the user holds right Ctrl, and the user saves left Ctrl+left Win as the new hotkey
- **THEN** the recording is cancelled
- **AND** nothing is inserted

#### Scenario: Update check turned on without restart
- **WHEN** "Check for updates automatically" is off, and the user turns it on and saves
- **THEN** an update check runs within 1 minute
- **AND** the application did not restart

#### Scenario: Update check turned off while a notice is shown
- **WHEN** the tray menu shows **Pisum Transcribe 1.2.0 is available…**, and the user turns off "Check for updates automatically" and saves
- **THEN** the tray menu no longer shows the update item
- **AND** no further update check runs
