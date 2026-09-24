## MODIFIED Requirements

### Requirement: Hotkey editor
The dictation section SHALL show the current push-to-talk hotkey and offer **Change…**. While changing, push-to-talk dictation SHALL be suspended, and the combination of keys the user holds together SHALL be captured when all of them are released. **Esc** SHALL cancel the change, and so SHALL the settings window losing focus. Because the hotkey distinguishes the left and right key of a pair, a key that exists on both sides of the keyboard SHALL be shown with its side.

The key names and the rule for a valid hotkey SHALL follow the platform:
- On Windows, a captured hotkey SHALL be rejected with a validation message unless it contains at least one of Ctrl, Alt, Shift, the Windows key, or a function key F1–F24. Keys SHALL be shown with Windows names, such as "Right Ctrl" or "Left Win", modifiers first in the order Ctrl, Alt, Shift, Win.
- On macOS, a captured hotkey SHALL be rejected with a validation message unless it contains at least one of Control, Option, Shift, Command, fn, or a function key F1–F24. Keys SHALL be shown with Mac names, such as "Right Command", "Left Option" or "fn", modifiers first in the order fn, Control, Option, Shift, Command.

On macOS, while the hotkey shown in the editor includes fn, whether saved or just captured, a hint SHALL say to set "Press 🌐 key to" in System Settings → Keyboard to "Do Nothing", because macOS otherwise also runs that action on every press.

#### Scenario: Record left Ctrl and left Win
- **WHEN** the user chooses **Change…** on Windows, holds the left Ctrl and left Win keys together and releases them
- **THEN** the hotkey field shows "Left Ctrl+Left Win"

#### Scenario: Record right Command on macOS
- **WHEN** the user chooses **Change…** on macOS, holds the right Command key and releases it
- **THEN** the hotkey field shows "Right Command"

#### Scenario: Record fn on macOS
- **WHEN** the user chooses **Change…** on macOS, holds the fn key and releases it
- **THEN** the hotkey field shows "fn"
- **AND** a hint says to set "Press 🌐 key to" to "Do Nothing"

#### Scenario: Reject a letter key
- **WHEN** the user chooses **Change…** and presses and releases the A key alone
- **THEN** a validation message says the hotkey must include a modifier or function key, named as the platform names them
- **AND** the previous hotkey is kept

#### Scenario: No dictation while recording a hotkey
- **WHEN** the hotkey editor is capturing and the user presses the current push-to-talk hotkey
- **THEN** no dictation recording starts

#### Scenario: Focus lost while recording a hotkey
- **WHEN** the user chooses **Change…** and switches to another application before pressing any key
- **THEN** the change is cancelled and the previous hotkey is kept
- **AND** pressing the current push-to-talk hotkey starts a dictation again
