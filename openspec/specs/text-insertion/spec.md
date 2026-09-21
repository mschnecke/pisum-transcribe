# text-insertion Specification

## Purpose

Defines how transcript text is delivered at the cursor of the application the user was working in, without losing the user's clipboard or typing into the wrong window.

## Requirements

### Requirement: Insertion settings
The application SHALL persist `textInsertion.method` (`clipboardPaste` or `typeText`, default `clipboardPaste`) and `textInsertion.restoreClipboard` (default `true`).

#### Scenario: Defaults
- **WHEN** no text insertion settings have been saved
- **THEN** the method is `clipboardPaste` and clipboard restore is enabled

### Requirement: Target window captured at recording start
The target of an insertion SHALL be the foreground window at the moment the recording started. Text SHALL be inserted only if that same window is still the foreground window immediately before the keystrokes are sent. If no window was in the foreground when the recording started, for example during a window switch, the text SHALL NOT be inserted; it SHALL be left on the clipboard, and the outcome SHALL report "target window changed".

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

### Requirement: Elevated target windows
When the target window belongs to a process running with higher privileges than Pisum Transcribe, the application SHALL NOT attempt keyboard or paste input. It SHALL leave the transcript on the clipboard and report "target window is elevated".

#### Scenario: Dictating into an administrator terminal
- **WHEN** the target window belongs to an elevated process and Pisum Transcribe is not elevated
- **THEN** no keystrokes are sent
- **AND** the transcript is left on the clipboard
- **AND** the outcome reports "target window is elevated"

### Requirement: Modifier keys released before input
Before sending any keystrokes, the application SHALL wait, for at most 2 seconds, until the modifier keys that would change them are released: Shift, Alt and Windows before a paste, and Shift, Ctrl, Alt and Windows before typing. A held Ctrl SHALL NOT delay a paste, because Ctrl+V stays Ctrl+V. If one of these modifiers is still held after 2 seconds, no keystrokes SHALL be sent, the transcript SHALL be left on the clipboard, and the outcome SHALL report "modifier keys held".

#### Scenario: User still holds Shift
- **WHEN** the transcript is ready while the user holds Shift and releases it 500 ms later
- **THEN** the text is inserted after Shift is released

#### Scenario: Modifier held too long
- **WHEN** a modifier key other than Ctrl stays held for more than 2 seconds after the transcript is ready
- **THEN** no keystrokes are sent
- **AND** the transcript is left on the clipboard
- **AND** the outcome reports "modifier keys held"

#### Scenario: Hotkey held again during a paste
- **WHEN** the method is `clipboardPaste`, the hotkey is right Ctrl, and the user holds right Ctrl again when the transcript is ready
- **THEN** the text is pasted without waiting for right Ctrl to be released

#### Scenario: Ctrl held before typing
- **WHEN** the method is `typeText` and the user holds Ctrl when the transcript is ready and releases it 500 ms later
- **THEN** the text is typed after Ctrl is released

### Requirement: Clipboard paste method
With `clipboardPaste`, the application SHALL place the transcript on the clipboard as Unicode text and send Ctrl+V to the target window. The inserted text SHALL be exactly the transcript, including non-ASCII characters such as ä, ö, ü, ß and €. If the clipboard content changes after the transcript was placed on it and before Ctrl+V is sent, the application SHALL type the text instead and SHALL NOT restore the clipboard.

#### Scenario: German umlauts
- **WHEN** the transcript "Grüße aus Köln – 5 €" is inserted with `clipboardPaste`
- **THEN** the target text field contains exactly "Grüße aus Köln – 5 €"

#### Scenario: User copies while the paste waits
- **WHEN** the transcript is on the clipboard, the application waits for the user to release Shift, and the user copies "new text" before releasing it
- **THEN** the transcript is typed into the target window
- **AND** the clipboard keeps "new text"

### Requirement: Clipboard restore
With `clipboardPaste` and `restoreClipboard` enabled, the application SHALL restore the clipboard contents from before the paste about 750 ms after sending Ctrl+V. Text content SHALL be restored exactly, and other formats on a best-effort basis. The application SHALL NOT restore when the clipboard content changed after the paste, for example because the user copied something. It SHALL NOT restore content that its source marked as sensitive by excluding it from clipboard monitoring, clipboard history or the cloud clipboard, such as a password copied from a password manager; the transcript then remains on the clipboard. Content that the application itself put back in an earlier restore SHALL NOT count as sensitive. If the clipboard is unavailable at restore time, the transcript SHALL remain on the clipboard and the outcome SHALL still be "inserted". With `restoreClipboard` disabled, the transcript SHALL remain on the clipboard.

#### Scenario: Previous text restored
- **WHEN** the clipboard contains "invoice 4711" and a transcript is pasted with restore enabled
- **THEN** about 750 ms later the clipboard contains "invoice 4711" again

#### Scenario: User copies during the restore delay
- **WHEN** the user copies "new text" after the paste but before the restore
- **THEN** the clipboard keeps "new text"

#### Scenario: Password not restored
- **WHEN** the clipboard holds a password that a password manager marked as excluded from clipboard monitoring, and a transcript is pasted with restore enabled
- **THEN** the password is not put back on the clipboard
- **AND** the clipboard holds the transcript

#### Scenario: Password marked only as excluded from history
- **WHEN** the clipboard holds a password that a password manager marked only as excluded from clipboard history and the cloud clipboard, and a transcript is pasted with restore enabled
- **THEN** the password is not put back on the clipboard

#### Scenario: Two dictations in a row
- **WHEN** the clipboard contains "invoice 4711" and two transcripts are pasted one after the other with restore enabled
- **THEN** after each restore the clipboard contains "invoice 4711"

### Requirement: Clipboard history exclusion
The transcript placed on the clipboard for a paste that will be restored SHALL be marked so that it is not added to Windows clipboard history or synchronized to the cloud clipboard. Content put back by a restore SHALL be marked so that it is not added to the clipboard history or synchronized to the cloud clipboard a second time.

#### Scenario: Clipboard history enabled
- **WHEN** Windows clipboard history is enabled and a transcript is pasted with restore enabled
- **THEN** the transcript does not appear in the Win+V clipboard history

#### Scenario: Restored content not added again
- **WHEN** Windows clipboard history is enabled, the user copies "invoice 4711", and a transcript is pasted with restore enabled
- **THEN** the Win+V clipboard history contains "invoice 4711" once

### Requirement: Busy clipboard fallback
When the clipboard cannot be opened because another process holds it, the application SHALL keep trying for about 1 second. If the clipboard is still unavailable while preparing a paste, the application SHALL insert the text with the type-text method instead.

#### Scenario: Clipboard locked by another application
- **WHEN** another process keeps the clipboard open for 2 seconds while a transcript is inserted with `clipboardPaste`
- **THEN** the text is inserted by typing
- **AND** the outcome reports "inserted"

### Requirement: Type text method
With `typeText`, the application SHALL send the transcript as Unicode character input. Every character, including characters outside the Basic Multilingual Plane, SHALL arrive as typed, and each line break SHALL be sent as the Enter key. Typing SHALL NOT modify the clipboard.

#### Scenario: Typing multi-line text
- **WHEN** the transcript "Hallo\nWelt 👋" is inserted with `typeText`
- **THEN** the target field contains "Hallo", a line break, and "Welt 👋"
- **AND** the clipboard content is unchanged

### Requirement: Insertion outcome
Every insertion SHALL report one outcome: "inserted", "target window changed", "target window is elevated", "modifier keys held" or "clipboard unavailable". The outcomes "target window changed", "target window is elevated" and "modifier keys held" SHALL leave the transcript on the clipboard, where it SHALL remain and not be restored or excluded from history. When such a fallback cannot place the transcript on the clipboard, because the clipboard stays unavailable for about 1 second, the outcome SHALL be "clipboard unavailable" instead, the transcript is not delivered, and the original reason SHALL be logged. When the clipboard is unavailable but still holds the transcript that was set for a paste, the transcript is delivered: the outcome SHALL be the original reason, and the transcript MAY stay excluded from history.

#### Scenario: Outcome for fallback
- **WHEN** an insertion ends with "target window changed"
- **THEN** the clipboard contains the transcript until the user replaces it

#### Scenario: Fallback with a busy clipboard
- **WHEN** the target window changed and another process keeps the clipboard open for 2 seconds
- **THEN** no keystrokes are sent
- **AND** the outcome reports "clipboard unavailable"

#### Scenario: Busy clipboard after the transcript was set for a paste
- **WHEN** the transcript was set for a paste, a modifier key stays held for more than 2 seconds, and another process then keeps the clipboard open for 2 seconds
- **THEN** no keystrokes are sent
- **AND** the clipboard contains the transcript
- **AND** the outcome reports "modifier keys held"

### Requirement: Pending clipboard restore
An insertion SHALL count as finished once the text is delivered or the fallback is reported. The clipboard restore after a paste SHALL NOT delay it. An insertion that starts while the restore of a previous paste is still pending SHALL wait for that restore before it reads or changes the clipboard, so the restore puts back the user's content and not the previous transcript. When the application exits while a restore is pending, the restore SHALL complete before the process ends, within the application's exit time limit.

#### Scenario: Next paste before the restore
- **WHEN** the clipboard contains "invoice 4711", a transcript is pasted with restore enabled, and a second transcript is ready to paste 300 ms later
- **THEN** the second insertion waits until the first restore has run
- **AND** after the second restore the clipboard contains "invoice 4711"

#### Scenario: Exit right after a paste
- **WHEN** the clipboard contains "invoice 4711", a transcript is pasted with restore enabled, and the user chooses **Exit** 200 ms later
- **THEN** the clipboard contains "invoice 4711" after the process has ended
