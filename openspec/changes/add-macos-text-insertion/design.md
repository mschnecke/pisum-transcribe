## Context

See proposal.md for the motivation. The decisions come from the section "Decided for later macOS changes" of the archived `add-macos-shell` design (2026-09-22), its spike M5, and explore mode on 2026-09-24. D-numbers of earlier designs are written "shell D11" and "setup D6".

**Current state:**
- `TextInserter` is platform-neutral. It depends on four seams: `IClipboardService`, `IKeyboardInput`, `IForegroundWindowTracker` and `TimeProvider`, plus an `isSelfElevated` flag. The order of its steps (wait for a pending restore, target check, elevated check, snapshot and set, modifier wait, final foreground gate, keystrokes, restore in the background) is what the spec requires, and none of it is Windows-specific.
- Windows implements the seams in `TextInsertion/Windows/`: `ForegroundWindowTracker` (HWND, pid, elevation), `Win32ClipboardService` (its own thread, about 1 s of retries on a busy clipboard, a `Pisum.Transcribe.Restored` marker so that restored content doesn't look sensitive), and `SharpHookKeyboardInput` (`SendInput`, `GetAsyncKeyState`).
- `InsertionTarget` is `(nint WindowHandle, int ProcessId, bool IsElevated)`. `DictationController` captures it at the press and passes it to the inserter.
- `IClipboardService` says "the Windows clipboard", and its `Try*` results mean "busy".
- On macOS, `AppHost.Create` registers neither `AddTextInsertion()` nor `AddDictation()`.
- The helper's `Pasteboard.swift` has `pisum_pasteboard_access_behavior` and `pisum_pasteboard_probe` (setup D6). The setup window's optional **Paste from other apps** row leads the user to *always allow*.

**Facts that shape this design:**
- **Spike M5:** with pasteboard privacy enforced, reading another app's content alerts and blocks the reading thread until the user answers. The *ask* state alerts at every read. Writing and `changeCount` never alert. macOS 27 without the developer preview reports 2 (always allow).
- **`NSPasteboard.h`:** `prepareForNewContentsWithOptions:` with `NSPasteboardContentsCurrentHostOnly` keeps an entry off Universal Clipboard.
- **Spike M3:** the hook reported SharpHook's own posted Cmd+V as simulated. Events posted with `CGEventPost` carry the poster's pid (recording's "Simulated key events ignored").
- **libuiohook at the commit SharpHook 8.0.0 pins** (`TolikPylypchuk/libuiohook@a41658f`, read on 2026-09-24):
  - `src/macos/dispatch_event.c` marks every key press, key release and modifier change as simulated (`MASK_EMULATED`) when `kCGEventSourceUnixProcessID` is nonzero. Hardware events carry 0, and macOS stamps the poster's pid on every posted event, so any event this app posts is ignored by the hook, however it is created.
  - `src/macos/post_event.c`'s `hook_post_text` sets the whole string on one key down and one key up, without chunks, so `SimulateTextEntry` loses everything past about 20 UTF-16 units.
  - `src/macos/input_hook.c` enables the tap again after `kCGEventTapDisabledByTimeout`.
- **`CGEventKeyboardSetUnicodeString`** carries at most about 20 UTF-16 units per event. Longer strings are cut off silently.
- **Text Input Sources** (`TISCopyCurrentKeyboardLayoutInputSource`, `TISGetInputSourceProperty`) belong on the main thread:
  - Since macOS 15, HIToolbox's input source functions have been gaining `dispatch_assert_queue` main-thread assertions, which end the process with `EXC_BREAKPOINT`. Reports say they are enforced on 26.5.
  - Dictation apps crashed on exactly this path: VoiceStudio (debpalash/VoiceStudio#2123, fixed in #2205 by moving the layout lookup, the paste and typing to the main queue) and OpenFlow (Dilan-B/OpenFlow#7).
  - A test on the development Mac on 2026-09-24 (macOS 27.0) didn't reproduce it: both functions returned the layout data from a background thread, in a bare tool and inside a running `NSApplication`. Whether the assertion fires depends on the path, so the design doesn't rely on that.
  - `UCKeyTranslate` works on the layout data it is given and needs no particular thread.
  - In the same test, `IsSecureEventInputEnabled` and `CGEventSourceFlagsState` worked from a background thread.

## Goals / Non-Goals

**Goals:**
- `ITextInserter` behaves on macOS as the `text-insertion` spec says, so that `add-macos-dictation` only has to register `AddDictation()` there and add the `SecureInputOn` case to its notification.
- `TextInserter` stays one class for both platforms. Windows behaves exactly as today.

**Non-Goals:**
- The tray hint *paused while secure input is on*, its 2 s poll, and the secure-input reason in the fallback notification (#20). `DictationController.NotifyOutcome` is a `switch` without a default, so until then the new outcome is only logged.
- A consumer of the inserter on macOS (#20).
- The macOS overlay's placement on the target's screen (#20, through the tracker; D1).
- Any change to the setup window's **Paste from other apps** row.

## Decisions

### D1: `InsertionTarget` carries an opaque window token, and the macOS tracker owns the AX element

`InsertionTarget` becomes `(nint Window, int ProcessId, bool IsElevated)`. `Window` is 0 for no window, as today. On Windows it is the HWND, so `ForegroundWindowTracker` and its tests only rename the field. On macOS it is a capture number that only the tracker understands.

`MacOS/MacForegroundWindowTracker`, through the Accessibility C API (`DllImport` of ApplicationServices):
- **Capture:**
  1. The system-wide element's `AXFocusedApplication`, and its pid (`AXUIElementGetPid`).
  2. That application's `AXFocusedWindow`.
  3. The tracker releases the element of the previous capture, keeps this one retained, and returns a new, nonzero capture number.
- **`IsForeground`:** the token is the current capture number, the pid matches, and `CFEqual` of the retained element with the focused window read again.
- **Any AX error** (no focused application, no window, `kAXErrorCannotComplete` on a timeout, a missing grant) gives the token 0, so the target counts as changed.
- **Messaging timeout:** `AXUIElementSetMessagingTimeout` on the system-wide element sets 0.25 s for every element. A hung target costs at most that much, twice.
- **`IsElevated`** is always `false` on macOS. There are no per-window privileges. Secure input takes that place (D5).
- The retained element is released when the tracker is disposed.

This holds because dictations never overlap: `DictationController` answers a press during `Processing` with `ShowBusy()` only, so every capture comes after the previous insertion, and the tracker only needs the latest element.

**The overlay's placement (hands off to #20).** `DictationFeedback.ShowStarting` passes the target's window value to `IOverlayPlatform.GetWorkArea`, which finds the target's monitor from it. On macOS that value is a capture number. #20's macOS overlay platform asks the tracker for the frame of the retained element (`AXPosition` and `AXSize`) and picks the screen that contains it, so the token stays opaque and no second capture is needed. This change doesn't add that method, because nothing on macOS would call it yet.

*Rejected:*
- **The element in the record, disposable.** `DictationController` would have to dispose every target, including on every error path, for a detail that only macOS has.
- **The `CGWindowID` from `CGWindowListCopyWindowInfo`.** It needs no AX and never hangs, but it guesses the focused window by z-order, and panels outside the normal window layer need extra rules. The shell design decided on the Accessibility API.

### D2: `MacClipboardService` on its own thread, over the helper by pasteboard name

`MacOS/MacClipboardService` implements `IClipboardService`:
- **One dedicated background thread** ("Pasteboard") runs every helper call, fed by a queue. The UI thread never touches the pasteboard, and if a read ever alerts, it blocks only that thread.
- **`SequenceNumber`** is the pasteboard's `changeCount`. The interface type widens from `uint` to `long`, and Windows widens its value. The helper's `changeCount` is safe from any thread, so the property reads it directly, as Windows reads `GetClipboardSequenceNumber`.
- **`TrySnapshotAsync`:**
  - It reads `pisum_pasteboard_access_behavior()` first. Only -1 (before 15.4) and 2 (always allow) allow the read. Otherwise it returns `null` without reading, and logs the state.
  - It then takes every item and every type with its data. A type without data is skipped. There is no size cap, as on Windows.
  - File promise types are left out: `com.apple.NSFilePromiseItemMetaData`, `com.apple.pasteboard.promised-file-url` and `com.apple.pasteboard.promised-file-content-type`. The known off-main-thread crash of NSPasteboard involves them, and a promise can't be restored anyway, because the source that would fulfil it may have moved on. Plain file URLs, text, images and rich text are kept. The helper filters them, so their data is never read.
  - `IsSensitive` is true when any item has `org.nspasteboard.ConcealedType` or `org.nspasteboard.TransientType` and none has the restored marker.
- **`TrySetTextAsync(text, excludeFromHistory)`:**
  - It clears the pasteboard and writes `public.utf8-plain-text`.
  - With `excludeFromHistory`, it first calls `prepareForNewContents(with: .currentHostOnly)` and adds `org.nspasteboard.TransientType`, `ConcealedType` and `AutoGeneratedType`.
- **`TryRestoreAsync`** clears and writes the saved items as they were, after `.currentHostOnly`, plus `org.nspasteboard.TransientType` and the restored marker `io.github.mschnecke.pisum-transcribe.restored` on the first item. An empty snapshot only clears.
- **`false` or `null`** now means "not possible right now". On Windows that's a busy clipboard. On macOS it's a helper that isn't available (`MacNativeLibrary.IsAvailable`), or, for the snapshot only, a read that isn't allowed. `TextInserter` already types when the snapshot is `null`. Its log message becomes neutral ("The clipboard could not be read or set, typing the text instead"), and each service logs its own reason.

`IClipboardService`'s docs describe both platforms, and "Busy clipboard fallback" becomes Windows-only in the spec.

*Rejected:*
- **Reading in the *default* state.** State 0 alerts at the first read. The setup window's probe exists so that this alert never appears during an insertion.
- **Only the text, not every type.** Windows restores every format it can read, and users copy images and files too.

### D3: The helper's pasteboard functions, ABI 3

New functions in `Pasteboard.swift`. Each takes a pasteboard name as UTF-8, or null for the general pasteboard, so that tests use their own pasteboards (D7). Each wraps its body in `autoreleasepool`, because it runs on a thread without a run loop.

- `pisum_pasteboard_change_count(name) -> Int64`
- `pisum_pasteboard_snapshot(name, uint8_t** buffer, int64_t* length) -> Int32`: 0 on success. The buffer is allocated with `malloc` and freed with `pisum_free`. It is flat: the item count, then per item its type count, then per type a length-prefixed UTF-8 name and length-prefixed data, all as little-endian `Int32` and `Int64` lengths. C# parses it into `MacClipboardSnapshot(IReadOnlyList<IReadOnlyList<(string Type, byte[] Data)>> Items, bool IsSensitive)` and decides `IsSensitive` itself, from the type names.
- `pisum_pasteboard_set_text(name, const char* utf8, Int32 exclude) -> Int32`
- `pisum_pasteboard_restore(name, const uint8_t* buffer, int64_t length, Int32 mark) -> Int32`: the same format as the snapshot. With `mark` 1, as for every restore, the helper adds the transient and restored markers and `.currentHostOnly`, so both ends stay in Swift. With `mark` 0 it writes the items as they are, which the Integration tests use to put a password manager's concealed item or a file promise on their pasteboard (added during implementation).
- `pisum_pasteboard_release(name) -> Int32`: `releaseGlobally()` of a named pasteboard, for tests. It refuses the general pasteboard.

`pisum_pasteboard_access_behavior` and `pisum_pasteboard_probe` don't change. The old library has none of the new functions, so `pisum_abi_version` and `MacNativeLibrary.ExpectedAbiVersion` go from 2 to 3 together. `CLAUDE.md`'s rule "called on the UI thread" gets a second exception: the pasteboard functions, which run on the pasteboard thread.

### D4: `MacKeyboardInput` posts CGEvents directly

`MacOS/MacKeyboardInput` implements `IKeyboardInput` with CoreGraphics through `DllImport`, without SharpHook's simulator:
- **`SendPaste`:**
  - It posts V down and V up at `kCGHIDEventTap`, both with `kCGEventFlagMaskCommand` set.
  - It posts no Command key event, so it never mixes with a right Command that the user still holds, and it can't match the hotkey.
- **The V key:**
  - The keycode that produces "v" with the Command modifier in the current layout (`UCKeyTranslate` over the layout's `kTISPropertyUnicodeKeyLayoutData`), or `kVK_ANSI_V` (9) when no key does, as in non-Latin layouts, where macOS falls back to the Latin layout itself.
  - Because Text Input Sources belong on the main thread (Context: the crashes of other dictation apps, although 27.0 didn't reproduce them), the keycode is computed on the UI thread through `IUiDispatcher`: once when the service starts, and again after each `com.apple.Carbon.TISNotifySelectedKeyboardInputSourceChanged` distributed notification (CFNotificationCenter, as recording uses for the screen lock). It is kept in a volatile field, so `SendPaste` stays synchronous and nothing slow runs after the final gate.
- **`TypeText`:**
  - It splits lines as Windows does. Each line is posted in chunks of at most 20 UTF-16 units that never split a surrogate pair, each chunk as a key down and a key up with `CGEventKeyboardSetUnicodeString` and no flags. Line breaks are `kVK_Return` down and up.
  - The chunk events carry keycode 0, as libuiohook's and most typing tools' do. `CGEvent.h` warns that "application frameworks may ignore the Unicode string in a keyboard event and do their own translation based on the virtual keycode". An app that does, such as a virtual machine guest or a remote desktop client, types "a" in place of each chunk. AppKit apps, Terminal, Electron and JetBrains IDEs read the string (checked by hand, D7).
  - *Rejected for now:* a key per character through a reverse `UCKeyTranslate` table, with Shift or Option. It would fix those apps for characters on the layout, but dead keys (^, ´ and ` on the German layout) would start a composition in them, 500 characters would take 1,000 events that may need pacing, and the table would go stale with a layout switch. It can be a later change if a real use needs it.
- **`AreModifiersDown(includeCommand)`:**
  - It reads `CGEventSourceFlagsState(kCGEventSourceStateHIDSystemState)`, as `MacHotkeyKeyState` does: Shift, Option (`Alternate`) and Control, plus Command when asked.
  - The interface's parameter is renamed from `includeControl` to `includePasteModifier`: Ctrl on Windows, Command on macOS.

*Rejected:* **SharpHook's `EventSimulator`:**
- Its text entry posts the whole string in one event, so a transcript longer than about 20 UTF-16 units loses its rest without an error (Context).
- It posts `VcV` as the fixed key position 9 plus a separate left Command press. That pastes the wrong shortcut with Dvorak, and it presses Command while the user may hold right Command.

### D5: Secure input at the final gate, behind `ISecureInput`

- `ISecureInput.IsEnabled`:
  - `MacOS/MacSecureInput` calls `IsSecureEventInputEnabled()` (Carbon, `DllImport`), a cheap call that is safe on any thread.
  - `Windows/NoSecureInput` returns `false`.
- `TextInserter` checks it right after the final `IsForeground` check. When it's on, the insertion falls back with `InsertionOutcome.SecureInputOn`, like `TargetWindowElevated`: the transcript is left on the clipboard as the user's content.
- #20's tray poll uses the same seam.

Secure input is system-wide, so an app that leaves it on by mistake would make every insertion fall back. The hotkey is dead in that state as well, so no dictation starts, and #20's tray hint names the cause.

*Rejected:* **Inserting anyway.** Posted events do reach a password field, and a dictated sentence there is never what the user wants.

### D6: Registration

`AddTextInsertion()` registers the macOS implementations under `#if !WINDOWS`:
- `MacForegroundWindowTracker`, `MacClipboardService`, `MacKeyboardInput`, `MacSecureInput`
- `TextInserter` with `isSelfElevated: false`, as a hosted service, as on Windows

`MacKeyboardInput` is also a hosted service, for the keycode at start and the notification. `AppHost.Create` calls `AddTextInsertion()` on both platforms. Nothing on macOS asks for `ITextInserter` until #20, but the hosted services start, and registration errors show now.

### D7: Tests and checks by hand

- **Unit, on both hosts:**
  - `TextInserterTests` with a fake `ISecureInput`: `SecureInputOn` at the final gate, only after the foreground check, and never before the modifier wait; a `null` snapshot types.
  - The existing tests with the renamed token and `long` sequence number.
  - The parser of the snapshot buffer, including an empty pasteboard and several items.
  - The chunking of `TypeText` (surrogate pairs at the 20-unit boundary, empty lines) behind a seam that records the posted strings.
- **macOS `Integration`** (the real helper, no desktop): each test uses its own named pasteboard (`io.github.mschnecke.pisum-transcribe.tests.<guid>`) and releases it afterwards.
  - Set, then read, including ä, ö, ü, ß, € and an emoji.
  - The exclusion markers present or absent.
  - A snapshot and restore round trip of two items with several types, with the markers after the restore.
  - A snapshot of an item that also has a file promise type leaves that type out and keeps the others.
  - The concealed marker makes it sensitive, and the restored marker doesn't.
  - `changeCount` rises on every write.
  - `IsSecureEventInputEnabled` reads `false` in the test host.
  - The layout lookup gives 9 for the US layout.
- **macOS `Hardware`** (`[Fact(Explicit = true)]`, in `DesktopCollection`):
  - TextEdit opens with a new document through `open -a TextEdit`, and the tests read the text area's `AXValue`.
  - They need the Accessibility grant of the terminal or IDE that runs them, and `Assert.SkipWhen` skips without it. They close the document without saving.
  - The tests:
    - Command+V of "Grüße aus Köln – 5 €"
    - typing 500 characters with emoji and line breaks
    - the tracker capturing TextEdit, and then seeing another document as changed
    - the whole `TextInserter` against the general pasteboard with restore, when its access behavior is -1 or 2
    - the keyboard hook reporting our posted Command+V as simulated, a guard against a later SharpHook that changes libuiohook's pid check (Context)
    - Dvorak, when that input source is enabled, and skipped otherwise
- **By hand, with the dev bundle** (nothing calls the inserter until #20, so these use the Hardware tests from a terminal with the grants, or wait for #20's checks):
  - Paste from Other Apps set to *ask*: the text is typed, no alert appears, and the pasteboard is unchanged.
  - A clipboard manager that honors the markers (Maccy, or Raycast's clipboard history): the transcript is missing from its history, and the restored content appears once.
  - Spotlight's clipboard history on macOS 26 or later: whether it honors the markers. Only recorded; the spec doesn't depend on it.
  - Universal Clipboard with an iPhone: the transcript isn't offered there.
  - A launcher panel (Spotlight, Raycast) as the target: recorded, and falls back to the clipboard when AX doesn't report it as the focused window.
  - 1Password: a copied password isn't restored.
  - Typing "Grüße aus Köln – 5 € 👋" over two lines into Terminal, iTerm2, VS Code and a JetBrains IDE: each gets the exact text. In a UTM or Parallels guest, if one is available, the result is only recorded, because keycode 0 is expected to type "a" there (D4).

## Risks / Trade-offs

- **[NSPasteboard isn't documented as safe off the main thread]** → explore mode on 2026-09-24:
  - The one documented crash is AppKit changing a pasteboard's type cache from two threads while file promises are on it (Wade Tregaskis, "NSPasteboard crashes due to unsafe, internal concurrent memory mutation when handling file promises"). The snapshot leaves promise types out (D2).
  - pisum-whisper's `MacOsClipboard` ships NSPasteboard calls from thread-pool threads, and spike M5 and setup D6's probe did the same without a crash.
  - One thread serializes every call of this app. The only concurrent user is AppKit on the main thread, for a copy or paste in a text box of the app's own windows at the very moment of an insertion, which needs focus in the target app.
  - *Rejected:* the main thread for every call. The snapshot of lazily provided data would freeze the menu bar icon and the overlay while the source renders it, and a setting changed to *ask* between the access check and the read would hold the UI thread behind macOS's alert.
  - If crashes appear anyway, the fallback is the main queue for the writes and `changeCount`, with the snapshot kept on the pasteboard thread.
- **[A lazily provided type makes the snapshot slow]** → the snapshot runs before the modifier wait, which overlaps with it, as on Windows. There is no cap, the same trade-off Windows makes.
- **[A hung target app delays the microphone]** → `DictationController` captures the target before it starts the recorder, because the overlay's placement needs the target. On macOS the capture is two AX calls of at most 0.25 s each, so with a hung app the recording starts up to 0.5 s late, and the first word may be cut. The transcript then goes to the clipboard anyway, because the target counts as no window. With an app that answers, the capture takes a few milliseconds. Accepted.
- **[The AX timeout also applies to other AX calls in the process]** → the app makes no other AX element calls. `AXIsProcessTrusted` isn't an element call.
- **[A launcher's non-activating panel isn't the focused application's focused window]** → the text is left on the clipboard with "target window changed", the safe side. It's a check by hand.
- **[Clipboard managers that ignore the nspasteboard.org markers record the transcript]** → the markers are the convention macOS clipboard managers follow. There is nothing stronger to use.
- **[`IsSecureEventInputEnabled` gets a main-thread assertion in a later macOS]** → it comes from HIToolbox, like the input source functions, but no report of an assertion exists, and the 27.0 test passed. If it asserts, `MacSecureInput` returns a value cached on the UI thread, which #20's 2 s poll already reads there, and refreshes it on the UI thread before the final gate.
- **[Apps that translate keycodes themselves type "a" for typed text]** → a known limitation of keycode 0 plus a Unicode string (D4), for virtual machine guests and remote desktop clients. Typing is the path when the method is `typeText`, when the user copies during the wait, and when restore is on but pasteboard access isn't *always allow*. In the last case the setup window's **Paste from other apps** row is the way out, because a paste works in those apps where their own clipboard sync does.
- **[The layout keycode is out of date for a moment after a layout switch]** → the notification recomputes it within milliseconds, and a paste right at the switch is unlikely.

## Migration Plan

There is no macOS release yet. The ABI moves from 2 to 3 in the same build as the C# side. On Windows, the renamed token field, the widened sequence number and the renamed modifier parameter are internal. Rollback is reverting the change.
