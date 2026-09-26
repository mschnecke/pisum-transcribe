## ADDED Requirements

### Requirement: Secure input
On macOS, when Secure Event Input is on right before the keystrokes would be sent, for example because a password field has focus, the application SHALL NOT attempt keyboard or paste input. It SHALL leave the transcript on the clipboard and report "secure input is on". The check SHALL happen together with the final check of the target window, with nothing slow between it and the keystrokes.

#### Scenario: Focus moved to a password field during the dictation
- **WHEN** the recording started in a browser window on macOS, and a password field of the same window has focus when the transcript is ready
- **THEN** no keystrokes are sent
- **AND** the transcript is left on the clipboard
- **AND** the outcome reports "secure input is on"

#### Scenario: Secure input off
- **WHEN** a text editor has focus on macOS and Secure Event Input is off when the transcript is ready
- **THEN** the text is inserted into the editor

## MODIFIED Requirements

### Requirement: Target window captured at recording start
The target of an insertion SHALL be the foreground window at the moment the recording started. Text SHALL be inserted only if that same window is still the foreground window immediately before the keystrokes are sent. If no window was in the foreground when the recording started, for example during a window switch, the text SHALL NOT be inserted; it SHALL be left on the clipboard, and the outcome SHALL report "target window changed".

On macOS, the foreground window SHALL be the focused window of the focused application. When the frontmost application doesn't report its focused window within a short time, for example because it hangs, it SHALL count as no window, so the text is left on the clipboard instead of the insertion waiting for the application.

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

### Requirement: Modifier keys released before input
Before sending any keystrokes, the application SHALL wait, for at most 2 seconds, until the modifier keys that would change them are released. On Windows these are Shift, Alt and Windows before a paste, and Shift, Ctrl, Alt and Windows before typing; a held Ctrl SHALL NOT delay a paste, because Ctrl+V stays Ctrl+V. On macOS these are Shift, Option and Control before a paste, and Shift, Option, Control and Command before typing; a held Command SHALL NOT delay a paste, because Command+V stays Command+V. If one of these modifiers is still held after 2 seconds, no keystrokes SHALL be sent, the transcript SHALL be left on the clipboard, and the outcome SHALL report "modifier keys held".

#### Scenario: User still holds Shift
- **WHEN** the transcript is ready while the user holds Shift and releases it 500 ms later
- **THEN** the text is inserted after Shift is released

#### Scenario: Modifier held too long
- **WHEN** a modifier key that delays the insertion stays held for more than 2 seconds after the transcript is ready
- **THEN** no keystrokes are sent
- **AND** the transcript is left on the clipboard
- **AND** the outcome reports "modifier keys held"

#### Scenario: Hotkey held again during a paste
- **WHEN** the method is `clipboardPaste`, the hotkey is right Ctrl, and the user holds right Ctrl again when the transcript is ready
- **THEN** the text is pasted without waiting for right Ctrl to be released

#### Scenario: Hotkey held again during a paste on macOS
- **WHEN** the method is `clipboardPaste` on macOS, the hotkey is right Command, and the user holds right Command again when the transcript is ready
- **THEN** the text is pasted without waiting for right Command to be released

#### Scenario: Ctrl held before typing
- **WHEN** the method is `typeText` and the user holds Ctrl when the transcript is ready and releases it 500 ms later
- **THEN** the text is typed after Ctrl is released

#### Scenario: Command held before typing on macOS
- **WHEN** the method is `typeText` on macOS and the user holds Command when the transcript is ready and releases it 500 ms later
- **THEN** the text is typed after Command is released

### Requirement: Clipboard paste method
With `clipboardPaste`, the application SHALL place the transcript on the clipboard as Unicode text and send the paste shortcut to the target window: Ctrl+V on Windows, and Command+V on macOS. On macOS, the V of Command+V SHALL be the key that produces "v" with Command in the current keyboard layout, so the paste works with layouts such as Dvorak; when no key of the layout does, the key of V on a US keyboard SHALL be used. The inserted text SHALL be exactly the transcript, including non-ASCII characters such as ä, ö, ü, ß and €. If the clipboard content changes after the transcript was placed on it and before the paste shortcut is sent, the application SHALL type the text instead and SHALL NOT restore the clipboard.

#### Scenario: German umlauts
- **WHEN** the transcript "Grüße aus Köln – 5 €" is inserted with `clipboardPaste`
- **THEN** the target text field contains exactly "Grüße aus Köln – 5 €"

#### Scenario: User copies while the paste waits
- **WHEN** the transcript is on the clipboard, the application waits for the user to release Shift, and the user copies "new text" before releasing it
- **THEN** the transcript is typed into the target window
- **AND** the clipboard keeps "new text"

#### Scenario: Dvorak layout on macOS
- **WHEN** the current keyboard layout on macOS is Dvorak and a transcript is inserted with `clipboardPaste`
- **THEN** the target text field contains the transcript

### Requirement: Clipboard restore
With `clipboardPaste` and `restoreClipboard` enabled, the application SHALL restore the clipboard contents from before the paste about 750 ms after sending the paste shortcut. Text content SHALL be restored exactly, and other formats on a best-effort basis. The application SHALL NOT restore when the clipboard content changed after the paste, for example because the user copied something. It SHALL NOT restore content that its source marked as sensitive, such as a password copied from a password manager; the transcript then remains on the clipboard. On Windows, sensitive content is marked as excluded from clipboard monitoring, clipboard history or the cloud clipboard. On macOS, it carries the nspasteboard.org concealed or transient marker. Content that the application itself put back in an earlier restore SHALL NOT count as sensitive. If the clipboard is unavailable at restore time, the transcript SHALL remain on the clipboard and the outcome SHALL still be "inserted". With `restoreClipboard` disabled, the transcript SHALL remain on the clipboard.

On macOS, taking the contents from before the paste is the only time an insertion reads the pasteboard, and an insertion SHALL NOT cause a macOS pasteboard alert. The application SHALL read the pasteboard only when the system allows it without asking the user: always allowed, or a macOS version without pasteboard privacy. Otherwise it SHALL NOT read the pasteboard, SHALL insert the text by typing, and SHALL leave the pasteboard unchanged. With `restoreClipboard` disabled, nothing is read, so the paste SHALL work whatever the pasteboard access setting is.

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

#### Scenario: Concealed password on macOS
- **WHEN** the pasteboard on macOS holds a password that a password manager marked with the nspasteboard.org concealed type, and a transcript is pasted with restore enabled
- **THEN** the password is not put back on the pasteboard
- **AND** the pasteboard holds the transcript

#### Scenario: Two dictations in a row
- **WHEN** the clipboard contains "invoice 4711" and two transcripts are pasted one after the other with restore enabled
- **THEN** after each restore the clipboard contains "invoice 4711"

#### Scenario: Several items restored on macOS
- **WHEN** the user copied two files in Finder on macOS and a transcript is pasted with restore enabled
- **THEN** after the restore the pasteboard holds both files again

#### Scenario: Pasteboard access set to ask on macOS
- **WHEN** Pisum Transcribe is set to ask under System Settings → Privacy & Security → Paste from Other Apps, the pasteboard contains "invoice 4711", and a transcript is inserted with `clipboardPaste` and restore enabled
- **THEN** no pasteboard alert appears
- **AND** the transcript is typed into the target window
- **AND** the pasteboard still contains "invoice 4711"
- **AND** the outcome reports "inserted"

#### Scenario: Pasteboard access denied, restore off on macOS
- **WHEN** Pisum Transcribe is denied under Paste from Other Apps on macOS, and a transcript is inserted with `clipboardPaste` and restore disabled
- **THEN** no pasteboard alert appears
- **AND** the transcript is pasted into the target window
- **AND** the pasteboard holds the transcript

### Requirement: Clipboard history exclusion
The transcript placed on the clipboard for a paste that will be restored SHALL be marked so that it is not added to clipboard history or synchronized to other devices. Content put back by a restore SHALL be marked the same way, so that it is not added to the clipboard history or synchronized a second time. On Windows, this covers Windows clipboard history and the cloud clipboard. On macOS, it covers clipboard managers that honor the nspasteboard.org markers, and Universal Clipboard.

#### Scenario: Clipboard history enabled
- **WHEN** Windows clipboard history is enabled and a transcript is pasted with restore enabled
- **THEN** the transcript does not appear in the Win+V clipboard history

#### Scenario: Restored content not added again
- **WHEN** Windows clipboard history is enabled, the user copies "invoice 4711", and a transcript is pasted with restore enabled
- **THEN** the Win+V clipboard history contains "invoice 4711" once

#### Scenario: Clipboard manager on macOS
- **WHEN** a clipboard manager that honors the nspasteboard.org markers runs on macOS, the user copies "invoice 4711", and a transcript is pasted with restore enabled
- **THEN** the clipboard manager's history doesn't contain the transcript
- **AND** it contains "invoice 4711" once

#### Scenario: Universal Clipboard on macOS
- **WHEN** Universal Clipboard is active between the Mac and another device, and a transcript is pasted with restore enabled
- **THEN** the transcript is not available to paste on the other device

### Requirement: Busy clipboard fallback
On Windows, when the clipboard cannot be opened because another process holds it, the application SHALL keep trying for about 1 second. If the clipboard is still unavailable while preparing a paste, the application SHALL insert the text with the type-text method instead. The macOS pasteboard has no lock, so this requirement applies to Windows only.

#### Scenario: Clipboard locked by another application
- **WHEN** another process keeps the clipboard open for 2 seconds on Windows while a transcript is inserted with `clipboardPaste`
- **THEN** the text is inserted by typing
- **AND** the outcome reports "inserted"

### Requirement: Type text method
With `typeText`, the application SHALL send the transcript as Unicode character input. Every character, including characters outside the Basic Multilingual Plane, SHALL arrive as typed and in order, also in a transcript longer than one keyboard event can carry, and each line break SHALL be sent as the Enter key (Return on macOS). Typing SHALL NOT modify the clipboard.

#### Scenario: Typing multi-line text
- **WHEN** the transcript "Hallo\nWelt 👋" is inserted with `typeText`
- **THEN** the target field contains "Hallo", a line break, and "Welt 👋"
- **AND** the clipboard content is unchanged

#### Scenario: Typing a long transcript
- **WHEN** a transcript of 500 characters with umlauts and emoji is inserted with `typeText`
- **THEN** the target field contains exactly the transcript

### Requirement: Insertion outcome
Every insertion SHALL report one outcome: "inserted", "target window changed", "target window is elevated", "secure input is on", "modifier keys held" or "clipboard unavailable". The outcomes "target window changed", "target window is elevated", "secure input is on" and "modifier keys held" SHALL leave the transcript on the clipboard, where it SHALL remain and not be restored or excluded from history. When such a fallback cannot place the transcript on the clipboard, because the clipboard stays unavailable for about 1 second, the outcome SHALL be "clipboard unavailable" instead, the transcript is not delivered, and the original reason SHALL be logged. When the clipboard is unavailable but still holds the transcript that was set for a paste, the transcript is delivered: the outcome SHALL be the original reason, and the transcript MAY stay excluded from history.

#### Scenario: Outcome for fallback
- **WHEN** an insertion ends with "target window changed"
- **THEN** the clipboard contains the transcript until the user replaces it

#### Scenario: Secure input fallback leaves the transcript as the user's content
- **WHEN** an insertion on macOS ends with "secure input is on" and a clipboard manager that honors the nspasteboard.org markers runs
- **THEN** the pasteboard contains the transcript
- **AND** the clipboard manager's history contains it

#### Scenario: Fallback with a busy clipboard
- **WHEN** the target window changed and another process keeps the clipboard open for 2 seconds
- **THEN** no keystrokes are sent
- **AND** the outcome reports "clipboard unavailable"

#### Scenario: Busy clipboard after the transcript was set for a paste
- **WHEN** the transcript was set for a paste, a modifier key stays held for more than 2 seconds, and another process then keeps the clipboard open for 2 seconds
- **THEN** no keystrokes are sent
- **AND** the clipboard contains the transcript
- **AND** the outcome reports "modifier keys held"
