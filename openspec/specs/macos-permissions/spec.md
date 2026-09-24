# macos-permissions Specification

## Purpose

Defines the permissions Pisum Transcribe needs on macOS: Accessibility, Microphone, Notifications and pasteboard access. It covers how the application asks for them up front in the setup window, how it follows their state, and how it restarts itself so that an Accessibility grant takes effect.

## Requirements

### Requirement: Permission rows in the setup window
On macOS, the setup window SHALL show one row per permission below the model part:
- **Accessibility**, required
- **Microphone**, required
- **Notifications**, optional
- **Paste from other apps**, optional

Each row SHALL show whether the permission is granted, not yet asked, or denied, and SHALL mark the required ones. A row SHALL update on its own within 2 seconds after the permission changes, including a change the user makes in System Settings, without any action in the window. The rows SHALL NOT block the model part: a model download SHALL run while the user grants permissions.

#### Scenario: Rows at the first start
- **WHEN** Pisum Transcribe starts on macOS for the first time
- **THEN** the setup window shows the rows Accessibility and Microphone as required and not granted
- **AND** it shows the rows Notifications and Paste from other apps as optional

#### Scenario: Grant made in System Settings
- **WHEN** the setup window is open and the user turns on Pisum Transcribe under System Settings → Privacy & Security → Accessibility
- **THEN** within 2 seconds the Accessibility row shows the permission as granted

#### Scenario: Download while granting
- **WHEN** a model download is running and the user grants the microphone permission
- **THEN** the download continues
- **AND** the Microphone row shows the permission as granted

### Requirement: Asking for Accessibility and Microphone
The application SHALL ask for Accessibility and for the microphone only when the user chooses **Allow…** in the permission's row, and never when the push-to-talk hotkey is pressed. For the microphone, **Allow…** SHALL show macOS's permission prompt when the permission wasn't asked yet, and SHALL open System Settings → Privacy & Security → Microphone when it was denied. For Accessibility, **Allow…** SHALL show macOS's Accessibility prompt, which leads to System Settings → Privacy & Security → Accessibility. The microphone prompt SHALL explain that Pisum Transcribe records while the hotkey is held.

#### Scenario: Microphone asked for the first time
- **WHEN** the microphone permission wasn't asked yet and the user chooses **Allow…** in the Microphone row
- **THEN** macOS asks whether Pisum Transcribe may use the microphone, with the application's explanation

#### Scenario: Microphone denied before
- **WHEN** the user denied the microphone permission and chooses **Allow…** in the Microphone row
- **THEN** System Settings opens at Privacy & Security → Microphone

#### Scenario: Accessibility asked
- **WHEN** the user chooses **Allow…** in the Accessibility row
- **THEN** macOS shows its Accessibility prompt, which opens System Settings → Privacy & Security → Accessibility

### Requirement: Microphone state from the system
The application SHALL read the microphone permission from macOS's authorization state, and SHALL NOT infer it from recorded audio, because macOS delivers silence and no error to an application whose microphone access was denied. A permission that is denied or restricted, for example by a device management profile, SHALL show as denied.

#### Scenario: Microphone denied
- **WHEN** the user turns off Pisum Transcribe under System Settings → Privacy & Security → Microphone
- **THEN** the Microphone row shows the permission as denied, without the microphone having been opened

### Requirement: Relaunch after the Accessibility grant
On macOS, when Accessibility turns from not granted to granted while the application runs, the application SHALL restart itself, so that the push-to-talk hotkey can use the grant, which macOS makes visible only to a new process. This SHALL hold whether the setup window is open or closed, and also when the grant was in place at start and was revoked while the application runs: from the revoke on, the grant SHALL count as not in effect until the restart, so the setup window SHALL NOT close as complete on a new grant alone. While a model download is running, the restart SHALL wait until the download has succeeded, failed or been cancelled. Before the restart, the setup window, when it is open, SHALL say for at least 3 seconds that Pisum Transcribe restarts. The restart SHALL end the application as **Quit Pisum Transcribe** does, and the new instance SHALL start once the old one has ended. After the restart, the setup window SHALL open again when the selected model isn't installed or a required permission is missing.

#### Scenario: Grant without a download
- **WHEN** no download is running and the user grants Accessibility
- **THEN** the setup window says that Pisum Transcribe restarts
- **AND** after at least 3 seconds the application ends and starts again, with exactly one menu bar icon

#### Scenario: Grant during a download
- **WHEN** a model download is running and the user grants Accessibility
- **THEN** the application doesn't restart while the download runs
- **AND** the Accessibility row says that Pisum Transcribe restarts when the download is finished
- **AND** after the download succeeds, the application restarts

#### Scenario: Setup still incomplete after the restart
- **WHEN** the application restarts after the Accessibility grant and the microphone permission is still missing
- **THEN** the setup window opens again after the restart

#### Scenario: Grant while the window is closed
- **WHEN** the setup window is closed and the user grants Accessibility in System Settings
- **THEN** the application restarts within 10 seconds

#### Scenario: Grant again after a revoke
- **WHEN** the application started with the Accessibility grant, the user revoked it while the application runs, and the user then grants it again through **Set up Pisum Transcribe…**
- **THEN** the setup window says that Pisum Transcribe restarts, and doesn't close as complete before that
- **AND** after the restart, holding the push-to-talk hotkey raises *pressed*

### Requirement: Set up menu item
On macOS, the menu bar menu SHALL contain one **Set up Pisum Transcribe…** item, in place of **Download model…**, that opens the setup window. It SHALL be shown while the selected model is not installed, or Accessibility or the microphone permission is not granted, and hidden otherwise. Whether the item is shown SHALL reflect the model and the permissions at the moment the menu opens, including a model file removed or a permission revoked while the application runs.

#### Scenario: Setup closed with a permission missing
- **WHEN** the selected model is installed and the user closes the setup window while the microphone permission is not granted
- **THEN** the menu bar menu shows **Set up Pisum Transcribe…**
- **AND** it doesn't show **Download model…**

#### Scenario: Setup closed without a model
- **WHEN** both required permissions are granted and the user closes the setup window without downloading
- **THEN** the menu bar menu shows **Set up Pisum Transcribe…**

#### Scenario: Permission revoked while running
- **WHEN** the selected model is installed, both required permissions are granted, and the user turns off the microphone permission in System Settings
- **THEN** the next time the user opens the menu bar menu, it shows **Set up Pisum Transcribe…**

#### Scenario: Setup complete
- **WHEN** the selected model is installed and both required permissions are granted
- **THEN** the menu bar menu shows neither **Set up Pisum Transcribe…** nor **Download model…**

### Requirement: Paste from other apps
On macOS, the **Paste from other apps** row SHALL show as granted when the system lets Pisum Transcribe read the pasteboard without asking, and on a macOS version without pasteboard privacy. When the system's pasteboard access is still at its default, the application SHALL read the pasteboard once while the setup window is open, so that macOS asks the user there and lists Pisum Transcribe in its settings, rather than during a dictation. When the access asks on every read, the row SHALL lead the user to the Paste from other apps setting in System Settings, where they can always allow it. When the access is denied, the row SHALL show as denied. The window SHALL NOT stay open because of this row.

#### Scenario: Pasteboard privacy not enforced
- **WHEN** the system reports that Pisum Transcribe may always read the pasteboard
- **THEN** the Paste from other apps row shows as granted, without reading the pasteboard

#### Scenario: Access asks on every read
- **WHEN** the system reports that reading the pasteboard asks each time
- **THEN** the Paste from other apps row offers to open the setting in System Settings

### Requirement: Permissions outside an app bundle
When the application runs on macOS without an app bundle, for example as the bare executable from a terminal, the setup window SHALL show no permission rows, the application SHALL NOT restart itself, and the menu SHALL show **Download model…** as on Windows instead of **Set up Pisum Transcribe…**, because the permissions then belong to the terminal and not to Pisum Transcribe. The application SHALL log that the permissions are skipped.

#### Scenario: Started without a bundle
- **WHEN** the application runs on macOS without an app bundle and the selected model is not installed
- **THEN** the setup window shows only the model part
- **AND** the menu shows **Download model…**
