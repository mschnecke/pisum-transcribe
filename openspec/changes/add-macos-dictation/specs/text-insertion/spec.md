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
