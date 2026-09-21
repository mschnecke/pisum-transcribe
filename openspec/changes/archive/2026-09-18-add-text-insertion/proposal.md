## Why

The result of a dictation has to appear at the cursor in whatever application the user was typing in: Office, browsers, Electron apps, IDEs, terminals. `docs/idea.md` recommends a two-layer approach. The primary layer is clipboard + Ctrl+V, restoring the user's clipboard afterwards. The alternative is typing the text as Unicode key events. It also warns about pasting into the wrong window and about elevated windows silently rejecting input. The workflow needs this capability before it can deliver text.

## What Changes

- Add a text insertion service with two methods, selected by the `textInsertion.method` setting:
  - **Clipboard paste** (default): put the text on the clipboard, send Ctrl+V, then restore the previous clipboard contents (`textInsertion.restoreClipboard`, default on).
  - **Type text**: send the text as Unicode keyboard input, with line breaks sent as Enter.
- Record the target window when the recording starts, and only insert if that window is still in the foreground. Otherwise leave the text on the clipboard and report why.
- Detect elevated (administrator) target windows, which silently reject input from a normal-privilege app. Do not attempt insertion; leave the text on the clipboard and report it.
- Wait briefly for the user to release modifier keys before sending keystrokes, so Ctrl+V is not turned into Ctrl+Shift+V or Ctrl+Alt+V. A held Ctrl does not delay a paste, because Ctrl+V stays Ctrl+V.
- Keep the temporary clipboard content out of Windows clipboard history and cloud clipboard, and do not add restored content to them a second time.
- Wait about 1 second when another process holds the clipboard, then fall back to typing. Report "clipboard unavailable" when a fallback cannot place the text on the clipboard either.
- Do not restore the clipboard if the user copied something new in the meantime, or if the previous content was marked as sensitive, such as a password from a password manager.

## Capabilities

### New Capabilities
- `text-insertion`: Delivering transcript text into the target application. Covers the insertion methods and setting, target-window and elevation checks, modifier handling, clipboard preservation and history exclusion, and outcomes reported to the caller.

### Modified Capabilities
<!-- None. -->

## Impact

- New code: `src/Pisum.Transcribe/TextInsertion/`.
- New dependency: `Microsoft.Windows.CsWin32` 0.3.333, a build-time source generator for the few Win32 calls (foreground window, process token elevation, async key state, clipboard sequence number). Keystrokes reuse SharpHook's `EventSimulator`, added in `add-push-to-talk-recording`.
- Settings: new `textInsertion` section (`method`, `restoreClipboard`).
- User-visible: the clipboard briefly holds the transcript during a paste. Non-text clipboard formats are restored on a best-effort basis, and content marked as sensitive is not restored.
- Depends on `scaffold-app-shell` and `add-push-to-talk-recording` (SharpHook). `add-dictation-workflow` depends on this change.
