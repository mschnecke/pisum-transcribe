# dictation Specification

## Purpose

Defines the end-to-end push-to-talk dictation experience: holding the hotkey records speech, releasing it inserts the transcribed or translated text at the cursor, and the user gets clear feedback throughout.

## Requirements

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

### Requirement: Dictation ends when the application exits
When the application exits while a dictation is in progress, the application SHALL end the dictation at once, as a cancelled dictation does, and before the process ends: a running recording SHALL be aborted, its audio discarded and the microphone released, the overlay SHALL hide, and nothing SHALL be inserted. This SHALL apply in every phase of the dictation, from the overlay's starting look to the transcription. On macOS, the tray menu's **Exit** is **Quit Pisum Transcribe**, and ending the user session is logging out of macOS.

#### Scenario: Exit during a recording
- **WHEN** a recording is running and the user chooses **Exit** from the tray while still holding the hotkey
- **THEN** the recording is aborted
- **AND** the overlay hides and Windows no longer shows Pisum Transcribe as using the microphone, before the process has ended
- **AND** nothing is inserted

#### Scenario: Quit during a recording on macOS
- **WHEN** a recording is running on macOS and the user chooses **Quit Pisum Transcribe** from the menu bar while still holding the hotkey
- **THEN** the recording is aborted
- **AND** the overlay hides and the orange microphone indicator in the menu bar goes off, before the process has ended
- **AND** nothing is inserted

#### Scenario: Exit while the microphone opens
- **WHEN** the overlay shows its starting look and the user chooses **Exit** before the microphone delivers audio
- **THEN** the overlay hides before the process has ended
- **AND** nothing is recorded or inserted

#### Scenario: Exit during transcription
- **WHEN** a dictation is being transcribed and the user chooses **Exit**
- **THEN** the overlay hides before the process has ended
- **AND** the process ends within 5 seconds

#### Scenario: Sign-out during a recording
- **WHEN** a recording is running and the user signs out of Windows or logs out of macOS
- **THEN** the recording is aborted before the process has ended
- **AND** nothing is inserted

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

### Requirement: Maximum recording duration
Recording SHALL stop automatically when it reaches the transcription engine's maximum input duration. The captured audio SHALL then be transcribed and inserted as if the hotkey had been released, and a notification SHALL tell the user that the maximum length was reached.

#### Scenario: Very long dictation
- **WHEN** the maximum input duration is 400 seconds and the user holds the hotkey for 410 seconds
- **THEN** recording stops at 400 seconds
- **AND** the 400 seconds of audio are transcribed and inserted
- **AND** a notification says the maximum dictation length was reached

### Requirement: Engine not ready
When the hotkey is pressed while the transcription engine is not ready, the application SHALL NOT record and SHALL show a notification explaining the reason: no model installed (pointing to **Download model…** in the tray menu on Windows, and to **Set up Pisum Transcribe…** in the menu bar on macOS), model still loading, or model failed to load. The same notification SHALL be shown at most once every 10 seconds.

#### Scenario: Press while model loads
- **WHEN** the engine status is `Loading` and the user presses the hotkey
- **THEN** no recording starts
- **AND** a notification says the model is still loading

#### Scenario: Repeated presses without model
- **WHEN** no model is installed and the user presses the hotkey three times within 5 seconds
- **THEN** exactly one notification is shown

#### Scenario: Press without model on macOS
- **WHEN** no model is installed on macOS and the user presses the hotkey
- **THEN** the notification points to **Set up Pisum Transcribe…** in the menu bar

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

### Requirement: Empty transcript
When the trimmed transcript is empty, the application SHALL insert nothing and the overlay SHALL show "No speech detected" for about 1.5 seconds.

#### Scenario: Silence recorded
- **WHEN** the user holds the hotkey for 2 seconds without speaking and the transcript is empty
- **THEN** nothing is inserted
- **AND** the overlay shows "No speech detected"

### Requirement: Insertion fallback notification
When text insertion ends with "target window changed", "target window is elevated", "secure input is on", "keystrokes not allowed" or "modifier keys held", the application SHALL show a notification that the text was copied to the clipboard, with the reason and the platform's paste shortcut, Ctrl+V on Windows and Command+V on macOS:
- "the active window changed"
- "the target window runs as administrator" (Windows only)
- "secure input is on, for example in a password field" (macOS only)
- "Accessibility access isn't in effect" (macOS only)
- "modifier keys were held"

When text insertion ends with "clipboard unavailable", the application SHALL show a notification that the text could not be inserted or copied because another application is using the clipboard.

#### Scenario: Window changed during transcription
- **WHEN** the user switches to another window before the transcript is ready
- **THEN** a notification says the text was copied to the clipboard because the active window changed

#### Scenario: Password field focused during transcription on macOS
- **WHEN** the recording started in a browser window on macOS, and a password field of that window has focus when the transcript is ready
- **THEN** nothing is typed into the password field
- **AND** a notification says the text was copied to the clipboard because secure input is on, and to paste it with Command+V

#### Scenario: Accessibility granted again during a transcription on macOS
- **WHEN** the user revokes the Accessibility grant during a transcription on macOS and grants it again before the transcript is ready
- **THEN** a notification says the text was copied to the clipboard because Accessibility access isn't in effect
- **AND** the application restarts after the dictation has ended

#### Scenario: Clipboard held by another application
- **WHEN** the user switches to another window before the transcript is ready and another application keeps the clipboard open
- **THEN** a notification says the text could not be inserted or copied because another application is using the clipboard

### Requirement: Error notifications
When recording or transcription fails, the application SHALL abandon the current dictation, hide the overlay, return to the ready state, and show a notification with the error's user-facing message. That includes blocked microphone access, no microphone, microphone muted, microphone not responding, microphone disconnected, language not supported by the model, and transcription failure. The message for blocked microphone access SHALL name the place to allow it: the microphone privacy settings on Windows, and *System Settings → Privacy & Security → Microphone* on macOS.

#### Scenario: Microphone access blocked
- **WHEN** Windows denies microphone access and the user presses the hotkey
- **THEN** a notification explains that microphone access is blocked and points to the microphone privacy settings
- **AND** the next hotkey press tries again

#### Scenario: Microphone access blocked on macOS
- **WHEN** the microphone permission of Pisum Transcribe is turned off in System Settings on macOS and the user presses the hotkey
- **THEN** a notification explains that microphone access is blocked and names *System Settings → Privacy & Security → Microphone*
- **AND** the orange microphone indicator doesn't turn on
- **AND** the next hotkey press tries again

#### Scenario: Language not supported
- **WHEN** the selected model is `canary-180m-flash-q8_0`, the source language is `pl`, and the user completes a recording
- **THEN** nothing is inserted
- **AND** a notification says the selected model does not support the configured language

### Requirement: Recording overlay behavior
The overlay SHALL stay above other windows. It SHALL NOT take keyboard focus or activate, SHALL let mouse clicks pass through to windows beneath it, SHALL NOT appear in the taskbar or Alt+Tab, and SHALL be placed at the bottom center of the work area of the monitor that contains the target window. When no target window was captured, it SHALL be placed on the primary monitor.

On macOS, the work area SHALL be the screen's area without the menu bar and the Dock. The overlay SHALL NOT appear in the Command+Tab switcher or in Mission Control, and SHALL show over a target application in full screen, on that application's Space.

#### Scenario: Overlay does not steal focus
- **WHEN** the overlay appears while the user dictates into a text editor
- **THEN** the text editor remains the foreground window with its caret

#### Scenario: Multi-monitor placement
- **WHEN** the target window is on the second monitor
- **THEN** the overlay appears at the bottom center of the second monitor

#### Scenario: Placement above the Dock on macOS
- **WHEN** the user dictates into a TextEdit window on a Mac with the Dock at the bottom of the screen
- **THEN** the overlay appears centered at the bottom of the screen, above the Dock and not covered by it

#### Scenario: Full-screen target on macOS
- **WHEN** the user dictates into TextEdit in full screen on its own Space
- **THEN** the overlay shows on that Space over TextEdit, and TextEdit keeps the focus
- **AND** the text is inserted into TextEdit

#### Scenario: Overlay absent from Mission Control
- **WHEN** the user opens Mission Control while the overlay is visible
- **THEN** Mission Control doesn't show the overlay as a window

### Requirement: Background activity
On macOS, the application SHALL keep macOS from throttling it through App Nap while it does work the user is waiting for: every dictation from the hotkey press until its insertion has ended, every model load with its warm-up (at start, after a model or backend change, and on the return to the GPU backend), every transcription, and the warm-up of voice activity detection. While the application is idle between dictations, it SHALL NOT keep macOS from throttling it.

#### Scenario: Model load at start
- **WHEN** the application starts on macOS with the menu bar icon as its only visible element and loads the selected model
- **THEN** macOS lists a user-initiated activity of Pisum Transcribe until the load and warm-up have ended

#### Scenario: Idle between dictations
- **WHEN** the model is loaded and no dictation has run for a minute
- **THEN** macOS lists no activity of Pisum Transcribe

### Requirement: Tray icon states
The tray icon SHALL show a distinct icon and tooltip for each state, and every tooltip SHALL contain the product name "Pisum Transcribe":
- ready: the tooltip keeps the engine's "Ready (<backend>)" text, such as "Ready (Vulkan)" on Windows, "Ready (Metal)" on macOS or "Ready (CPU)"
- recording: "Recording…"
- transcribing: "Transcribing…"
- unavailable: no model installed, model loading, or model failed, with the reason in the tooltip. On macOS, also while the push-to-talk hotkey can't work: while the Accessibility grant isn't in effect, with "Accessibility access needed for the hotkey", and while Secure Event Input is on, with "Paused while secure input is on". When several reasons apply, the tooltip SHALL name the first of: the model's reason, the Accessibility grant, secure input.

The icon SHALL be a monochrome microphone. In the ready state it SHALL be drawn in the taskbar's foreground color: dark on a light taskbar and light on a dark one. In the unavailable state it SHALL be the same icon, dimmed. In the recording state it SHALL be red, and in the transcribing state amber, in colors that stay readable on a light and a dark taskbar. When the user switches the taskbar between light and dark, the icon SHALL follow without a restart of the application. On macOS, the tray icon is the menu bar icon, and the menu bar takes the taskbar's place in these rules.

While no dictation is in progress, the tray SHALL show the engine's current status, including a status that changed during the dictation that just ended. On macOS, a change of secure input SHALL show in the tray within 3 seconds while no dictation is in progress.

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

#### Scenario: Accessibility grant not in effect on macOS
- **WHEN** the model is loaded on macOS and the user revokes the Accessibility grant of Pisum Transcribe while it runs
- **THEN** the menu bar icon shows the unavailable state with "Accessibility access needed for the hotkey" in its tooltip

#### Scenario: Secure input on macOS
- **WHEN** the model is loaded on macOS, no dictation is in progress, and the user turns on Secure Keyboard Entry in Terminal with Terminal in front
- **THEN** within 3 seconds the menu bar icon shows the unavailable state with "Paused while secure input is on" in its tooltip
- **AND** no notification is shown
- **AND** after the user turns Secure Keyboard Entry off, the icon returns to the ready state within 3 seconds

#### Scenario: Engine reloads during a dictation
- **WHEN** the backend is `auto` and the GPU device is lost while a dictation is transcribed on the GPU backend, so the engine reloads the model on the CPU backend and transcribes the dictation again there
- **THEN** the tray icon stays in the transcribing state and the overlay keeps showing "Transcribing…" until the transcript is inserted
- **AND** after the dictation ends, the tray icon shows the ready state with "Ready (CPU)"

#### Scenario: Engine returns to the GPU after a long dictation
- **WHEN** the backend is `auto` and a dictation of 300 seconds runs out of GPU memory on the GPU backend, so the engine transcribes it again on the CPU backend and then reloads the model on the GPU backend
- **THEN** the tray icon stays in the transcribing state and the overlay keeps showing "Transcribing…" until the transcript is inserted
- **AND** after the dictation ends, the tray icon shows the ready state with "Ready (CPU)" until the engine has reloaded the model on the GPU backend, and then "Ready (Vulkan)" on Windows or "Ready (Metal)" on macOS
- **AND** a hotkey press during that reload starts a recording, and that dictation's text is inserted once it has been transcribed

### Requirement: Dictation data retention
Recorded audio and transcript text SHALL exist only in memory for the duration of a dictation and SHALL be discarded once it completes, is cancelled or fails. Neither SHALL be written to disk or logs, except that the transcript is placed on the clipboard by text insertion.

#### Scenario: After a dictation
- **WHEN** a dictation completes
- **THEN** no file containing its audio or transcript has been created by the application
