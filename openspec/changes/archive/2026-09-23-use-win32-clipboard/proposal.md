## Why

The clipboard is the last part of text insertion that depends on WPF. `WpfClipboardService` runs WPF's OLE clipboard on a thread with its own WPF dispatcher. The Avalonia shell (`move-windows-shell-to-avalonia`) removes WPF, and Avalonia's own clipboard can't do what text insertion needs: enumerate every format for a snapshot, and write the formats that keep a transcript out of clipboard history. This change moves the clipboard onto the Win32 API behind the existing `IClipboardService`, with no change in behavior, while WPF still runs. It carries no regret even if the Avalonia spike fails.

## What Changes

- **`Win32ClipboardService`** replaces `WpfClipboardService` behind the same `IClipboardService`:
  - It uses `OpenClipboard`, `EnumClipboardFormats`, `GetClipboardData` and `SetClipboardData` through CsWin32, on a dedicated thread with a message-only window as the clipboard owner.
  - Snapshot and restore, clipboard history exclusion, the busy-clipboard fallback of about 1 second, and the sequence number behave as today.
- **`ClipboardSnapshot`** stops holding WPF's `DataObject`. It becomes an abstract record with only `IsSensitive`, and `Win32ClipboardSnapshot` holds the copied formats. `TextInserter` only reads `IsSensitive` and hands the snapshot back, so it doesn't change.
- **Tests:** the clipboard hardware tests run against the new service. `TextInsertion/TestWindow` becomes a plain Win32 window, so the desktop tests no longer depend on WPF.
- No visible change, and no spec change.
- Not included: any other part of text insertion, and any macOS clipboard, which comes with `add-macos-text-insertion`.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
<!-- None. The text-insertion requirements hold unchanged, so `.openspec.yaml` sets `skip_specs: true`. -->

## Impact

- **Code:**
  - `TextInsertion/WpfClipboardService.cs` goes. `TextInsertion/Windows/Win32ClipboardService.cs` and `TextInsertion/Windows/Win32ClipboardSnapshot.cs` come.
  - `TextInsertion/ClipboardSnapshot.cs` becomes abstract.
  - `TextInsertionServiceCollectionExtensions` registers the new service.
  - `NativeMethods.txt` gains the clipboard, global memory and message-only window functions, and a `// Test helpers` section with the few functions only the tests call.
- **Tests:**
  - `WpfClipboardServiceHardwareTests` becomes `Win32ClipboardServiceHardwareTests`, with the same cases and one new round-trip case.
  - `RawClipboard` and `TestWindow` stop using WPF and call the app's generated `PInvoke`.
  - `TextInserterTests` and `TextInserterHardwareTests` stop using `DataObject`.
- **Dependencies:** none.
- **User-visible:** nothing.
- **Unblocks:** `move-windows-shell-to-avalonia`, which can then remove WPF.
