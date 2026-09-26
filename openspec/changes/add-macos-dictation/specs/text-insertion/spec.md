## ADDED Requirements

### Requirement: Keystroke permission
On macOS, when the application isn't allowed to send keystrokes right before it would send them, the application SHALL NOT attempt keyboard or paste input. It SHALL leave the transcript on the clipboard and report "keystrokes not allowed". This is the case after the Accessibility grant was revoked while the application runs, including when it was granted again, because macOS lets only a new process send keystrokes with a new grant. The check SHALL happen together with the final check of the target window, with nothing slow between it and the keystrokes. On Windows this outcome never occurs.

#### Scenario: Accessibility revoked and granted again during a dictation
- **WHEN** the user revokes the Accessibility grant of Pisum Transcribe during a transcription on macOS and grants it again before the transcript is ready
- **THEN** no keystrokes are sent
- **AND** the transcript is left on the clipboard
- **AND** the outcome reports "keystrokes not allowed"

#### Scenario: Grant in effect since start
- **WHEN** Pisum Transcribe started on macOS with the Accessibility grant and a text editor has focus when the transcript is ready
- **THEN** the text is inserted into the editor

## MODIFIED Requirements

### Requirement: Target window captured at recording start
The target of an insertion SHALL be the foreground window at the moment the recording started. Text SHALL be inserted only if that same window is still the foreground window immediately before the keystrokes are sent. If no window was in the foreground when the recording started, for example during a window switch, the text SHALL NOT be inserted; it SHALL be left on the clipboard, and the outcome SHALL report "target window changed".

On macOS, the foreground window SHALL be the focused window of the focused application. When the frontmost application doesn't report its focused window within a short time, for example because it hangs, it SHALL count as no window, so the text is left on the clipboard instead of the insertion waiting for the application. When macOS names no focused application, as for Electron apps such as Visual Studio Code until their accessibility is switched on, the frontmost application SHALL be the owner of the frontmost normal window, and its focused window SHALL be the foreground window.

#### Scenario: Focus unchanged
- **WHEN** the user dictates into a text editor and the editor is still in the foreground after transcription
- **THEN** the text is inserted into the editor

#### Scenario: User switched windows during transcription
- **WHEN** the recording started in a text editor and a browser is in the foreground when the transcript is ready
- **THEN** no keystrokes are sent
- **AND** the transcript is left on the clipboard
- **AND** the outcome reports "target window changed"

#### Scenario: No foreground window at recording start
- **WHEN** no window was in the foreground when the recording started, and no window is in the foreground when the transcript is ready
- **THEN** no keystrokes are sent
- **AND** the transcript is left on the clipboard
- **AND** the outcome reports "target window changed"

#### Scenario: User switched windows while modifier keys were released
- **WHEN** the recording started in a text editor, the application waits for the user to release Shift, and the user switches to a browser before releasing it
- **THEN** no keystrokes are sent
- **AND** the transcript is left on the clipboard
- **AND** the outcome reports "target window changed"

#### Scenario: Another window of the same application on macOS
- **WHEN** the recording started in one TextEdit document on macOS, and another TextEdit document is focused when the transcript is ready
- **THEN** no keystrokes are sent
- **AND** the transcript is left on the clipboard
- **AND** the outcome reports "target window changed"

#### Scenario: Frontmost application doesn't answer on macOS
- **WHEN** the frontmost application on macOS doesn't respond when the recording starts
- **THEN** the dictation isn't held up waiting for it
- **AND** the transcript is left on the clipboard
- **AND** the outcome reports "target window changed"

#### Scenario: Electron application on macOS
- **WHEN** the user dictates into an editor tab of Visual Studio Code on macOS, started fresh, so macOS names no focused application
- **THEN** the text is inserted into the editor tab

### Requirement: Insertion outcome
Every insertion SHALL report one outcome: "inserted", "target window changed", "target window is elevated", "secure input is on", "keystrokes not allowed", "modifier keys held" or "clipboard unavailable". The outcomes "target window changed", "target window is elevated", "secure input is on", "keystrokes not allowed" and "modifier keys held" SHALL leave the transcript on the clipboard, where it SHALL remain and not be restored or excluded from history. When such a fallback cannot place the transcript on the clipboard, because the clipboard stays unavailable for about 1 second, the outcome SHALL be "clipboard unavailable" instead, the transcript is not delivered, and the original reason SHALL be logged. When the clipboard is unavailable but still holds the transcript that was set for a paste, the transcript is delivered: the outcome SHALL be the original reason, and the transcript MAY stay excluded from history.

#### Scenario: Outcome for fallback
- **WHEN** an insertion ends with "target window changed"
- **THEN** the clipboard contains the transcript until the user replaces it

#### Scenario: Secure input fallback leaves the transcript as the user's content
- **WHEN** an insertion on macOS ends with "secure input is on" and a clipboard manager that honors the nspasteboard.org markers runs
- **THEN** the pasteboard contains the transcript
- **AND** the clipboard manager's history contains it

#### Scenario: Keystrokes not allowed with clipboard restore
- **WHEN** clipboard restore is on, the user copied "invoice 4711" before the dictation, and an insertion on macOS ends with "keystrokes not allowed"
- **THEN** the pasteboard contains the transcript, not "invoice 4711"

#### Scenario: Fallback with a busy clipboard
- **WHEN** the target window changed and another process keeps the clipboard open for 2 seconds
- **THEN** no keystrokes are sent
- **AND** the outcome reports "clipboard unavailable"

#### Scenario: Busy clipboard after the transcript was set for a paste
- **WHEN** the transcript was set for a paste, a modifier key stays held for more than 2 seconds, and another process then keeps the clipboard open for 2 seconds
- **THEN** no keystrokes are sent
- **AND** the clipboard contains the transcript
- **AND** the outcome reports "modifier keys held"
