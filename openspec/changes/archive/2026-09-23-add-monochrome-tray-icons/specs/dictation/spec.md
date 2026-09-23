## MODIFIED Requirements

### Requirement: Tray icon states
The tray icon SHALL show a distinct icon and tooltip for each state, and every tooltip SHALL contain the product name "Pisum Transcribe":
- ready: the tooltip keeps the engine's "Ready (<backend>)" text
- recording: "Recording…"
- transcribing: "Transcribing…"
- unavailable: no model installed, model loading, or model failed, with the reason in the tooltip

The icon SHALL be a monochrome microphone. In the ready state it SHALL be drawn in the taskbar's foreground color: dark on a light taskbar and light on a dark one. In the unavailable state it SHALL be the same icon, dimmed. In the recording state it SHALL be red, and in the transcribing state amber, in colors that stay readable on a light and a dark taskbar. When the user switches the taskbar between light and dark, the icon SHALL follow without a restart of the application.

While no dictation is in progress, the tray SHALL show the engine's current status, including a status that changed during the dictation that just ended.

#### Scenario: State follows dictation
- **WHEN** a dictation goes from recording to transcribing to done
- **THEN** the tray icon changes from recording to transcribing to ready
- **AND** each tooltip contains "Pisum Transcribe"

#### Scenario: Icon follows the taskbar's mode
- **WHEN** the tray shows the ready state on a light taskbar and the user switches Windows to a dark taskbar
- **THEN** the ready icon changes from dark to light without a restart of the application

#### Scenario: Unavailable icon is dimmed
- **WHEN** no model is installed
- **THEN** the tray icon is the ready icon, dimmed, and the tooltip names the reason

#### Scenario: Engine reloads during a dictation
- **WHEN** the backend is `auto` and the GPU device is lost while a dictation is transcribed on Vulkan, so the engine reloads the model on the CPU backend and transcribes the dictation again there
- **THEN** the tray icon stays in the transcribing state and the overlay keeps showing "Transcribing…" until the transcript is inserted
- **AND** after the dictation ends, the tray icon shows the ready state with "Ready (CPU)"

#### Scenario: Engine returns to the GPU after a long dictation
- **WHEN** the backend is `auto` and a dictation of 300 seconds runs out of GPU memory on Vulkan, so the engine transcribes it again on the CPU backend and then reloads the model on the Vulkan backend
- **THEN** the tray icon stays in the transcribing state and the overlay keeps showing "Transcribing…" until the transcript is inserted
- **AND** after the dictation ends, the tray icon shows the ready state with "Ready (CPU)" until the engine has reloaded the model on the Vulkan backend, and then "Ready (Vulkan)"
- **AND** a hotkey press during that reload starts a recording, and that dictation's text is inserted once it has been transcribed
