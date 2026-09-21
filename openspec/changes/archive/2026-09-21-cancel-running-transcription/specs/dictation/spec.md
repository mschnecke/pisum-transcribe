## ADDED Requirements

### Requirement: Cancel a transcription
When the tray menu opens while a dictation is being transcribed, it SHALL show a **Cancel transcription** item. At any other time, including during a recording, the item SHALL NOT be shown.

Choosing the item before the dictation's transcript is ready SHALL cancel the dictation: the audio SHALL be discarded, nothing SHALL be inserted, the overlay SHALL hide at once without a message, the tray SHALL show the engine's current status, and no notification SHALL be shown. This SHALL apply from the end of the recording until the transcript is ready: while the silence is trimmed, while the transcription waits for the engine, while it runs, and while the engine reloads the model on the CPU backend after a Vulkan backend failure. Cancelling SHALL NOT change the engine status or the loaded model, and a reload in progress SHALL continue.

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
- **WHEN** the backend is `auto`, the Vulkan backend failed while a dictation was transcribed, and the user chooses **Cancel transcription** while the engine reloads the model on the CPU backend
- **THEN** the overlay hides at once and nothing is inserted
- **AND** the tray shows that the model is loading, and then the ready state with "Ready (CPU)" once the reload has finished
- **AND** a hotkey press before the reload has finished shows the notification that the model is still loading
- **AND** the cancelled dictation is not transcribed again

#### Scenario: Transcript ready before the choice
- **WHEN** the user opens the tray menu while a dictation is being transcribed, and its transcript is ready before the user chooses **Cancel transcription**
- **THEN** the transcript is delivered as for any dictation
- **AND** choosing the item has no effect

#### Scenario: Next dictation while the engine finishes the cancelled one
- **WHEN** the user cancels the transcription of a long dictation while the engine cannot stop it yet, and then records a short dictation
- **THEN** the new recording starts at the press
- **AND** the new dictation is transcribed once the engine has stopped the cancelled transcription
- **AND** its text is inserted

## MODIFIED Requirements

### Requirement: Busy handling
When the hotkey is pressed while a previous dictation is still being transcribed or inserted, the application SHALL NOT start a new recording, and the overlay SHALL briefly show "Still processing…". A dictation SHALL count as finished once its text is delivered, its fallback is reported, or it is cancelled. The clipboard restore after a paste SHALL NOT delay the next dictation.

#### Scenario: Press during transcription
- **WHEN** a dictation is being transcribed and the user presses the hotkey again
- **THEN** no new recording starts
- **AND** the overlay shows "Still processing…"
- **AND** the first dictation completes normally

#### Scenario: Press right after the text appears
- **WHEN** a transcript was just pasted with clipboard restore enabled and the user presses the hotkey 200 ms later
- **THEN** a new recording starts
- **AND** the overlay does not show "Still processing…"

#### Scenario: Press right after a cancel
- **WHEN** the engine is ready, the user chooses **Cancel transcription** and presses the hotkey 200 ms later
- **THEN** a new recording starts
- **AND** the overlay does not show "Still processing…"
