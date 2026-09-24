## MODIFIED Requirements

### Requirement: Cancel a transcription
When the tray menu opens while a dictation is being transcribed, it SHALL show a **Cancel transcription** item. At any other time, including during a recording, the item SHALL NOT be shown.

Choosing the item before the dictation's transcript is ready SHALL cancel the dictation: the audio SHALL be discarded, nothing SHALL be inserted, the overlay SHALL hide at once without a message, the tray SHALL show the engine's current status, and no notification SHALL be shown. This SHALL apply from the end of the recording until the transcript is ready: while the silence is trimmed, while the transcription waits for the engine, while it runs, and while the engine reloads the model on the CPU backend after a GPU backend failure. Cancelling SHALL NOT change the engine status or the loaded model, and a reload in progress SHALL continue.

Once the transcript is ready, choosing the item SHALL have no effect, and the transcript SHALL be delivered as for any dictation.

After a cancel, the next hotkey press SHALL be handled as any press at the engine's current status: it SHALL start a new recording when the engine is ready, and SHALL follow "Engine not ready" otherwise. The new recording SHALL NOT wait for the cancelled transcription. When the engine is still finishing the cancelled transcription, the new dictation SHALL be transcribed once the engine has stopped it.

#### Scenario: Cancel a long transcription on the CPU
- **WHEN** the backend is `cpu`, a dictation of 300 seconds is being transcribed, and the user opens the tray menu and chooses **Cancel transcription**
- **THEN** the overlay hides at once without a message
- **AND** the tray icon shows the ready state
- **AND** nothing is inserted and no notification is shown
- **AND** the next hotkey press starts a new recording

#### Scenario: Item hidden outside a transcription
- **WHEN** the user opens the tray menu while no dictation is in progress, or while a recording runs
- **THEN** the menu shows no **Cancel transcription** item

#### Scenario: Cancel while the engine reloads on the CPU
- **WHEN** the backend is `auto`, the GPU device was lost while a dictation was transcribed on the GPU backend, and the user chooses **Cancel transcription** while the engine reloads the model on the CPU backend
- **THEN** the overlay hides at once and nothing is inserted
- **AND** the tray shows that the model is loading, and then the ready state with "Ready (CPU)" once the reload has finished
- **AND** a hotkey press before the reload has finished shows the notification that the model is still loading
- **AND** the cancelled dictation is not transcribed again

#### Scenario: Cancel while the engine reloads on the CPU after running out of GPU memory
- **WHEN** the backend is `auto`, a dictation of 300 seconds ran out of GPU memory on the GPU backend, and the user chooses **Cancel transcription** while the engine reloads the model on the CPU backend
- **THEN** the overlay hides at once and nothing is inserted
- **AND** the tray shows that the model is loading until the reload on the CPU backend has finished
- **AND** the tray then shows the ready state with "Ready (CPU)" while the engine reloads the model on the GPU backend, and "Ready (Vulkan)" on Windows or "Ready (Metal)" on macOS once that reload has finished
- **AND** a hotkey press before the reload on the CPU backend has finished shows the notification that the model is still loading, and a later press starts a recording
- **AND** the cancelled dictation is not transcribed again

#### Scenario: Next dictation while the engine returns to the GPU
- **WHEN** the backend is `auto`, a dictation of 300 seconds ran out of GPU memory on the GPU backend, the user cancels it while it is transcribed again on the CPU backend, and then records a short dictation while the engine finishes the cancelled transcription or reloads the model on the GPU backend
- **THEN** the new recording starts at the press
- **AND** the new dictation is transcribed once the engine has stopped the cancelled transcription
- **AND** its text is inserted, and no notification that the model is still loading is shown

#### Scenario: Transcript ready before the choice
- **WHEN** the user opens the tray menu while a dictation is being transcribed, and its transcript is ready before the user chooses **Cancel transcription**
- **THEN** the transcript is delivered as for any dictation
- **AND** choosing the item has no effect

#### Scenario: Next dictation while the engine finishes the cancelled one
- **WHEN** the user cancels the transcription of a long dictation while the engine cannot stop it yet, and then records a short dictation
- **THEN** the new recording starts at the press
- **AND** the new dictation is transcribed once the engine has stopped the cancelled transcription
- **AND** its text is inserted

### Requirement: Tray icon states
The tray icon SHALL show a distinct icon and tooltip for each state, and every tooltip SHALL contain the product name "Pisum Transcribe":
- ready: the tooltip keeps the engine's "Ready (<backend>)" text, such as "Ready (Vulkan)" on Windows, "Ready (Metal)" on macOS or "Ready (CPU)"
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
- **WHEN** the backend is `auto` and the GPU device is lost while a dictation is transcribed on the GPU backend, so the engine reloads the model on the CPU backend and transcribes the dictation again there
- **THEN** the tray icon stays in the transcribing state and the overlay keeps showing "Transcribing…" until the transcript is inserted
- **AND** after the dictation ends, the tray icon shows the ready state with "Ready (CPU)"

#### Scenario: Engine returns to the GPU after a long dictation
- **WHEN** the backend is `auto` and a dictation of 300 seconds runs out of GPU memory on the GPU backend, so the engine transcribes it again on the CPU backend and then reloads the model on the GPU backend
- **THEN** the tray icon stays in the transcribing state and the overlay keeps showing "Transcribing…" until the transcript is inserted
- **AND** after the dictation ends, the tray icon shows the ready state with "Ready (CPU)" until the engine has reloaded the model on the GPU backend, and then "Ready (Vulkan)" on Windows or "Ready (Metal)" on macOS
- **AND** a hotkey press during that reload starts a recording, and that dictation's text is inserted once it has been transcribed

