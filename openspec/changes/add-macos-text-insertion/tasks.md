The order follows the dependencies. First the platform-neutral seams, then the helper and the macOS implementations, then the registration and the checks. Windows must stay green after every group: `dotnet test Pisum.Transcribe.slnx` on Windows runs unchanged.

## 1. Shared seams

- [ ] 1.1 Change `InsertionTarget` to an opaque `Window` token (D1). On Windows it stays the HWND, set by `ForegroundWindowTracker`, and `TextInserter` and `DictationController` use the new name. Verify: `dotnet build` passes for both frameworks, and `TextInserterTests`, `DictationControllerTests` and the Windows tracker tests pass.
- [ ] 1.2 Widen `IClipboardService.SequenceNumber` to `long`, describe the interface for both platforms, and make `TextInserter`'s log for a `null` snapshot or a failed set neutral (D2). Verify: `TextInserterTests` pass unchanged apart from the type.
- [ ] 1.3 Rename `IKeyboardInput.AreModifiersDown`'s parameter to `includePasteModifier` (D4). Verify: `SharpHookKeyboardInputTests` and `TextInserterTests` pass.
- [ ] 1.4 Add `InsertionOutcome.SecureInputOn`, `ISecureInput`, and `Windows/NoSecureInput`. `TextInserter` checks it right after the final `IsForeground` and falls back with `SecureInputOn` (D5, spec `text-insertion` "Secure input" and "Insertion outcome"). Verify: new `TextInserterTests` with a fake `ISecureInput`: the outcome and the transcript on the clipboard without exclusion; no check before the modifier wait; the foreground check wins when both apply; and no keystrokes.

## 2. The Swift helper

- [ ] 2.1 Add `pisum_pasteboard_change_count`, `pisum_pasteboard_snapshot`, `pisum_pasteboard_set_text`, `pisum_pasteboard_restore` and `pisum_pasteboard_release` to `Pasteboard.swift`, by pasteboard name, each in an `autoreleasepool`, with the buffer format and markers of D3. Raise `pisum_abi_version` and `MacNativeLibrary.ExpectedAbiVersion` to 3, and add the functions to `Hosting/MacOS/PisumMac`. Verify: `MacNativeLibraryIntegrationTests` see ABI 3, and the build compiles the dylib.
- [ ] 2.2 Add macOS `Integration` tests for the pasteboard functions on uniquely named pasteboards (D7). Verify: set and read with ä, ö, ü, ß, € and an emoji; the markers with and without exclusion; a round trip of two items with several types; the restored markers; a snapshot of an item that also has a file promise type leaves that type out and keeps the others (D2); `changeCount` rising; release refusing the general pasteboard.

## 3. The macOS implementations

- [ ] 3.1 Add `TextInsertion/MacOS/MacClipboardService` on its own pasteboard thread (D2): the access-behavior gate before the snapshot, the snapshot parser, `IsSensitive` from the concealed or transient type without the restored marker, set, restore, and `false` or `null` when the helper isn't available. Add the `TextInsertion/MacOS/` entries to both `.csproj.DotSettings` files. Verify: unit tests of the parser (an empty pasteboard, several items) and of `IsSensitive`; macOS `Integration` tests of the service on a named pasteboard, including a `null` snapshot for the access states 0, 1 and 3 through a fake reader, without a read (spec "Clipboard restore", "Clipboard history exclusion").
- [ ] 3.2 Add `TextInsertion/MacOS/MacForegroundWindowTracker` (D1): AX through `DllImport`, the 0.25 s messaging timeout, the retained element of the latest capture, `CFEqual` in `IsForeground`, token 0 on any AX error, and release on dispose. Verify: a unit test of the token bookkeeping behind a fake AX reader: an old token doesn't match, an error gives 0, and each element is released once (spec "Target window captured at recording start").
- [ ] 3.3 Add `TextInsertion/MacOS/MacKeyboardInput` (D4): V with the Command flag, the layout keycode computed on the UI thread at start and after the input source notification, text in surrogate-safe chunks of 20 UTF-16 units, Return for line breaks, and modifiers from the HID flags state. Verify: unit tests of the chunking (a surrogate pair at the boundary, empty lines) through a recording seam; a macOS `Integration` test that finds keycode 9 for the US layout; unit tests of the modifier mask with and without Command (spec "Modifier keys released before input", "Clipboard paste method", "Type text method").
- [ ] 3.4 Add `TextInsertion/MacOS/MacSecureInput` on `IsSecureEventInputEnabled` (D5). Verify: a macOS `Integration` test reads `false` in the test host.

## 4. Registration and end to end

- [ ] 4.1 Register the macOS implementations in `AddTextInsertion()`, and call it on both platforms in `AppHost.Create` (D6). Verify: the macOS host-building test resolves `ITextInserter` and every hosted service, and `TextInsertionServiceCollectionExtensionsTests` still pass on Windows.
- [ ] 4.2 Add the macOS `Hardware` tests with TextEdit, in `DesktopCollection`, skipped without the Accessibility grant (D7): Command+V of "Grüße aus Köln – 5 €"; typing 500 characters with emoji and line breaks; the tracker's capture and a changed document; `TextInserter` with restore against the general pasteboard when its access behavior is -1 or 2; the hook reporting our Command+V as simulated; Dvorak when that source is enabled. Verify: they pass with `--explicit on` from a terminal with the Accessibility grant.
- [ ] 4.3 Run the checks by hand of D7 on the dev Mac: the *ask* state, a clipboard manager, Spotlight's clipboard history, Universal Clipboard, a launcher panel, 1Password, and typing "Grüße aus Köln – 5 € 👋" over two lines into Terminal, iTerm2, VS Code, a JetBrains IDE and, if one is available, a UTM or Parallels guest. Verify: each result noted in the PR; every app except the guest gets the exact text. Spotlight, the launcher panel and the guest are only recorded.

## 5. Docs and validation

- [ ] 5.1 Update the docs:
  - `CLAUDE.md`: `TextInsertion/MacOS/` in the layout, text insertion in the macOS registration of `AppHost.Create`, the pasteboard functions as the second exception to the UI-thread rule, and ABI 3
  - `docs/roadmap.md`: the change done, with #20 picking up the tray hint and the `SecureInputOn` notification

  Verify: the texts match the code.
- [ ] 5.2 Run `openspec validate add-macos-text-insertion --strict`, and `dotnet build` plus `dotnet test Pisum.Transcribe.slnx` on the Mac and in CI on Windows. Verify: all pass.
