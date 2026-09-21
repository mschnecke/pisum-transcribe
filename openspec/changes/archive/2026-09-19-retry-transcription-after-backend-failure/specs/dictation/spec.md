## MODIFIED Requirements

### Requirement: Tray icon states
The tray icon SHALL show a distinct icon and tooltip for each state, and every tooltip SHALL contain the product name "Pisum Transcribe":
- ready: the tooltip keeps the engine's "Ready (<backend>)" text
- recording: "Recording…"
- transcribing: "Transcribing…"
- unavailable: no model installed, model loading, or model failed, with the reason in the tooltip

While no dictation is in progress, the tray SHALL show the engine's current status, including a status that changed during the dictation that just ended.

#### Scenario: State follows dictation
- **WHEN** a dictation goes from recording to transcribing to done
- **THEN** the tray icon changes from recording to transcribing to ready
- **AND** each tooltip contains "Pisum Transcribe"

#### Scenario: Engine reloads during a dictation
- **WHEN** the backend is `auto` and the Vulkan backend fails while a dictation is transcribed, so the engine reloads the model on the CPU backend and transcribes the dictation again there
- **THEN** the tray icon stays in the transcribing state and the overlay keeps showing "Transcribing…" until the transcript is inserted
- **AND** after the dictation ends, the tray icon shows the ready state with "Ready (CPU)"
