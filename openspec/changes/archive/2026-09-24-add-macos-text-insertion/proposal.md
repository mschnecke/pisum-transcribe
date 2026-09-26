## Why

On macOS nothing inserts a transcript yet: `AddTextInsertion()` registers no implementation outside Windows, and the shared `TextInserter` depends on an HWND, the Win32 clipboard and SharpHook's `SendInput`. Dictation on the Mac (`add-macos-dictation`, #20) needs paste with restore that respects macOS's pasteboard privacy, keeps transcripts out of clipboard managers and Universal Clipboard, and never types into a password field. This change brings text insertion to macOS behind the existing `TextInserter`.

Tracked in GitHub issue #19. The decisions come from the section "Decided for later macOS changes" of the archived `add-macos-shell` design, from spike M5, and from explore mode on 2026-09-24.

## What Changes

- **Paste with restore stays the method on macOS,** through NSPasteboard in the Swift helper and a posted Command+V.
  - The snapshot before a paste is the only read of the pasteboard. It is taken only while `NSPasteboard.accessBehavior` allows reading without an alert (always allow, or a macOS before 15.4). Otherwise that dictation is typed instead, so the user's clipboard stays untouched. With restore off, the paste needs no read and works in every state.
  - The pasteboard is never read on the UI thread, because macOS's alert blocks the reading thread.
  - The snapshot keeps every item and every type, as Windows keeps every format. Content marked with the nspasteboard.org Concealed or Transient type counts as sensitive and is not restored.
  - Command+V uses the key that types "v" with Command in the current keyboard layout, so Dvorak and similar layouts paste too.
- **Clipboard history exclusion on macOS:** a transcript set for a restored paste, and content put back by a restore, carry the nspasteboard.org markers for clipboard managers and stay off Universal Clipboard (`currentHostOnly`).
- **Secure input:** when Secure Event Input is on right before the keystrokes, for example because focus moved to a password field during the dictation, no keystrokes are sent, the transcript is left on the clipboard, and the new outcome "secure input is on" is reported. On Windows this outcome never occurs.
- **The target on macOS** is the focused application's pid and its focused window, read through the Accessibility API with a short timeout. A target app that doesn't answer counts as no window.
- **Typing on macOS** posts Unicode keyboard events in chunks, with line breaks as Return.
- **Modifier keys on macOS:** Shift, Option and Control delay a paste, and a held Command doesn't, because Command+V stays Command+V. Before typing, Command counts too.
- "Busy clipboard fallback" becomes Windows-only, because NSPasteboard has no lock.
- **Not included** (`add-macos-dictation`, #20):
  - the tray hint *paused while secure input is on*, with its 2 s poll
  - the secure-input reason in the `dictation` fallback notification
  - connecting the inserter to the dictation on macOS
- **Windows doesn't change,** apart from internal seams: the window token in `InsertionTarget`, the secure-input check, which always reports off, and platform-neutral clipboard documentation.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `text-insertion`:
  - "Target window captured at recording start": the focused window of the frontmost app on macOS, and no answer counts as no window.
  - "Secure input" (new, next to "Elevated target windows"): no keystrokes while Secure Event Input is on.
  - "Modifier keys released before input": the Mac modifier set, with Command instead of Ctrl.
  - "Clipboard paste method": Command+V from the current layout, and the pasteboard read allowed only without an alert.
  - "Clipboard restore": the sensitive markers on macOS.
  - "Clipboard history exclusion": clipboard managers and Universal Clipboard on macOS.
  - "Busy clipboard fallback": Windows-only.
  - "Type text method": Return for line breaks on macOS.
  - "Insertion outcome": the outcome "secure input is on".

## Impact

- **Depends on:** `add-macos-recording` (#17), which is merged. It uses the Accessibility grant, the pasteboard access state and the hook's handling of posted events.
- **Code:**
  - `TextInsertion/`: `InsertionTarget` gets an opaque window token instead of an HWND; `InsertionOutcome.SecureInputOn`; an `ISecureInput` seam checked at the final gate in `TextInserter`; `IClipboardService` documented per platform.
  - `TextInsertion/MacOS/`: `MacForegroundWindowTracker` (AX), `MacClipboardService` (its own pasteboard thread), `MacKeyboardInput` (CGEvent, `UCKeyTranslate`), `MacSecureInput` (`IsSecureEventInputEnabled`).
  - `Hosting/AppHost`: `AddTextInsertion()` on macOS. Nothing consumes it until #20.
- **Swift helper:** pasteboard snapshot, set, restore and change count, each by pasteboard name. `pisum_abi_version` and `MacNativeLibrary.ExpectedAbiVersion` go from 2 to 3. The pasteboard functions run on the pasteboard thread, not the UI thread.
- **Tests:**
  - unit tests for `TextInserter`'s new paths
  - macOS `Integration` tests of the pasteboard functions on uniquely named pasteboards
  - macOS `Hardware` tests that paste and type into TextEdit and read the result through Accessibility; they need the Accessibility grant of the terminal or IDE and skip without it
- **Docs:** `CLAUDE.md` (layout, the macOS registration, the pasteboard threading exception, ABI 3) and `docs/roadmap.md`.
