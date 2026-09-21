## Purpose

Defines the end-to-end push-to-talk dictation experience: holding the hotkey records speech, releasing it inserts the transcribed or translated text at the cursor, and the user gets clear feedback throughout.

## ADDED Requirements

### Requirement: Start recording on hotkey press
When the push-to-talk hotkey is pressed, no dictation is in progress and the transcription engine is ready, the application SHALL remember the current foreground window as the insertion target, show the recording overlay at once in its starting look, and start recording. When the microphone delivers audio, the overlay SHALL show "Recording" with an elapsed-time counter, and the tray icon SHALL switch to the recording state.

#### Scenario: Press while ready
- **WHEN** the engine is ready, the user is typing in a text editor and presses the hotkey
- **THEN** the overlay appears at once in its starting look
- **AND** once the microphone delivers audio, the overlay shows "Recording" with an elapsed-time counter
- **AND** the tray icon shows the recording state

#### Scenario: Slow microphone
- **WHEN** the default recording device is a Bluetooth headset that needs 1 second before it delivers audio, and the user presses the hotkey
- **THEN** the overlay shows its starting look for about 1 second
- **AND** then shows "Recording" with the elapsed-time counter starting at 0:00

### Requirement: Transcribe and insert on release
When the hotkey is released during a recording that lasted at least 300 ms, the application SHALL stop recording, transcribe the audio with the saved task, source language and target language, trim leading and trailing whitespace from the result, and insert the text into the remembered target window.

#### Scenario: Successful German to English dictation
- **WHEN** the settings are task `translate`, source `de`, target `en`, and the user holds the hotkey, says "Ich komme morgen um neun Uhr", and releases the hotkey in a text editor
- **THEN** the overlay shows "Transcribing…"
- **AND** an English translation is inserted at the editor's cursor
- **AND** the overlay hides and the tray icon returns to the ready state

### Requirement: Minimum recording duration
A recording shorter than 300 ms, counted from when the microphone starts delivering audio, SHALL be discarded without transcription, insertion or notification.

#### Scenario: Accidental tap
- **WHEN** the user presses and releases the hotkey within 200 ms
- **THEN** nothing is transcribed or inserted
- **AND** the overlay hides without a message

#### Scenario: Release before audio flows
- **WHEN** the user releases the hotkey while the overlay still shows its starting look
- **THEN** nothing is transcribed or inserted
- **AND** the overlay hides without a message

### Requirement: Cancelled dictation
When the hotkey signals *cancelled* during a recording, the application SHALL abort the recording, discard the audio, hide the overlay and insert nothing.

#### Scenario: User presses another key while holding
- **WHEN** a recording is running and the user presses the C key while holding the hotkey
- **THEN** the recording is aborted
- **AND** nothing is inserted

### Requirement: Maximum recording duration
Recording SHALL stop automatically when it reaches the transcription engine's maximum input duration. The captured audio SHALL then be transcribed and inserted as if the hotkey had been released, and a notification SHALL tell the user that the maximum length was reached.

#### Scenario: Very long dictation
- **WHEN** the maximum input duration is 400 seconds and the user holds the hotkey for 410 seconds
- **THEN** recording stops at 400 seconds
- **AND** the 400 seconds of audio are transcribed and inserted
- **AND** a notification says the maximum dictation length was reached

### Requirement: Engine not ready
When the hotkey is pressed while the transcription engine is not ready, the application SHALL NOT record and SHALL show a notification explaining the reason: no model installed (pointing to **Download model…**), model still loading, or model failed to load. The same notification SHALL be shown at most once every 10 seconds.

#### Scenario: Press while model loads
- **WHEN** the engine status is `Loading` and the user presses the hotkey
- **THEN** no recording starts
- **AND** a notification says the model is still loading

#### Scenario: Repeated presses without model
- **WHEN** no model is installed and the user presses the hotkey three times within 5 seconds
- **THEN** exactly one notification is shown

### Requirement: Busy handling
When the hotkey is pressed while a previous dictation is still being transcribed or inserted, the application SHALL NOT start a new recording, and the overlay SHALL briefly show "Still processing…". A dictation SHALL count as finished once its text is delivered or its fallback is reported. The clipboard restore after a paste SHALL NOT delay the next dictation.

#### Scenario: Press during transcription
- **WHEN** a dictation is being transcribed and the user presses the hotkey again
- **THEN** no new recording starts
- **AND** the overlay shows "Still processing…"
- **AND** the first dictation completes normally

#### Scenario: Press right after the text appears
- **WHEN** a transcript was just pasted with clipboard restore enabled and the user presses the hotkey 200 ms later
- **THEN** a new recording starts
- **AND** the overlay does not show "Still processing…"

### Requirement: Empty transcript
When the trimmed transcript is empty, the application SHALL insert nothing and the overlay SHALL show "No speech detected" for about 1.5 seconds.

#### Scenario: Silence recorded
- **WHEN** the user holds the hotkey for 2 seconds without speaking and the transcript is empty
- **THEN** nothing is inserted
- **AND** the overlay shows "No speech detected"

### Requirement: Insertion fallback notification
When text insertion ends with "target window changed", "target window is elevated" or "modifier keys held", the application SHALL show a notification that the text was copied to the clipboard, with the reason:
- "the active window changed"
- "the target window runs as administrator"
- "modifier keys were held"

When text insertion ends with "clipboard unavailable", the application SHALL show a notification that the text could not be inserted or copied because another application is using the clipboard.

#### Scenario: Window changed during transcription
- **WHEN** the user switches to another window before the transcript is ready
- **THEN** a notification says the text was copied to the clipboard because the active window changed

#### Scenario: Clipboard held by another application
- **WHEN** the user switches to another window before the transcript is ready and another application keeps the clipboard open
- **THEN** a notification says the text could not be inserted or copied because another application is using the clipboard

### Requirement: Error notifications
When recording or transcription fails, the application SHALL abandon the current dictation, hide the overlay, return to the ready state, and show a notification with the error's user-facing message. That includes blocked microphone access (pointing to the Windows microphone privacy settings), no microphone, microphone muted, microphone not responding, microphone disconnected, language not supported by the model, and transcription failure.

#### Scenario: Microphone access blocked
- **WHEN** Windows denies microphone access and the user presses the hotkey
- **THEN** a notification explains that microphone access is blocked and points to the microphone privacy settings
- **AND** the next hotkey press tries again

#### Scenario: Language not supported
- **WHEN** the selected model is `canary-180m-flash-q8_0`, the source language is `pl`, and the user completes a recording
- **THEN** nothing is inserted
- **AND** a notification says the selected model does not support the configured language

### Requirement: Recording overlay behavior
The overlay SHALL stay above other windows. It SHALL NOT take keyboard focus or activate, SHALL let mouse clicks pass through to windows beneath it, SHALL NOT appear in the taskbar or Alt+Tab, and SHALL be placed at the bottom center of the work area of the monitor that contains the target window.

#### Scenario: Overlay does not steal focus
- **WHEN** the overlay appears while the user dictates into a text editor
- **THEN** the text editor remains the foreground window with its caret

#### Scenario: Multi-monitor placement
- **WHEN** the target window is on the second monitor
- **THEN** the overlay appears at the bottom center of the second monitor

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
- **WHEN** the Vulkan backend fails while a dictation is transcribed and the engine reloads the model on the CPU backend
- **THEN** after the dictation ends, the tray icon shows the unavailable state with a tooltip saying the model is loading
- **AND** once the model is loaded, the tray icon shows the ready state with "Ready (CPU)"

### Requirement: Dictation data retention
Recorded audio and transcript text SHALL exist only in memory for the duration of a dictation and SHALL be discarded once it completes, is cancelled or fails. Neither SHALL be written to disk or logs, except that the transcript is placed on the clipboard by text insertion.

#### Scenario: After a dictation
- **WHEN** a dictation completes
- **THEN** no file containing its audio or transcript has been created by the application
