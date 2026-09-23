## 1. The Win32 clipboard service

- [x] 1.1 Add the clipboard, global-memory and window functions to `NativeMethods.txt`: `OpenClipboard`, `CloseClipboard`, `EmptyClipboard`, `EnumClipboardFormats`, `GetClipboardData`, `SetClipboardData`, `RegisterClipboardFormat`, `GlobalAlloc`, `GlobalLock`, `GlobalUnlock`, `GlobalSize`, `GlobalFree`, `CreateWindowEx`, `DestroyWindow`, `GetMessage`, `DispatchMessage`, `PostThreadMessage`, `GetCurrentThreadId`, `MsgWaitForMultipleObjectsEx` and `PeekMessage`, plus `HWND_MESSAGE`. Verify: `dotnet build Pisum.Transcribe.slnx` passes and CsWin32 generates them in `Windows.Win32.PInvoke`.
- [x] 1.2 Add `TextInsertion/Windows/Win32ClipboardService : IClipboardService, IDisposable` (design D1). It has:
  - its own thread with a message-only `STATIC` window as the clipboard owner, and a work queue woken with `PostThreadMessage`, with `TaskCompletionSource` results; `Dispose` posts `WM_QUIT`
  - `SequenceNumber` from `GetClipboardSequenceNumber`
  - `OpenClipboard` retried 10 times, 100 ms apart, with a wait that handles sent messages
  - `TrySetTextAsync` writing `CF_UNICODETEXT`, plus the three history formats when exclusion is asked for
  - the failure handling of D1: no exceptions for Win32 failures, the clipboard emptied again after a write that fails partway, and the "busy" and "could not" log entries with the Win32 error code

  Verify: `Win32ClipboardServiceHardwareTests`, the renamed `WpfClipboardServiceHardwareTests`, pass these cases against the new service in `DesktopCollection`:
  - `TrySetTextAsync_ExcludeFromHistory_SetsTextAndExclusionFormats`
  - `TrySetTextAsync_NotExcludedFromHistory_SetsTextWithoutExclusionFormats`
  - `TrySetTextAsync_ClipboardHeldOpenByAnotherThread_ReturnsFalseAfterAboutOneSecond`
- [x] 1.3 Make `ClipboardSnapshot` an abstract record with only `IsSensitive` (design D3):
  - Add `TextInsertion/Windows/Win32ClipboardSnapshot`, which holds the copied formats as an ordered list of format ID and bytes.
  - `WpfClipboardService` keeps its `DataObject` in a private subclass until task 2.1 deletes it.
  - `TextInserterTests` use a small test subclass that holds a string instead of `DataObject`.

  Verify: `dotnet build Pisum.Transcribe.slnx` passes, and `TextInserterTests` pass.
- [x] 1.4 Move `RawClipboard` from WPF and its `DllImport`s to the app's generated `PInvoke` (design D2). It uses only functions from task 1.1:
  - It gets `SetText`, and a `Set` for raw formats with their bytes, next to `Read`, `GetText` and `Hold`.
  - `TextInserterHardwareTests` and `Win32ClipboardServiceHardwareTests` use them instead of `DataObject`.

  Verify: the cases of task 1.2 and `TextInserterHardwareTests` pass.
- [x] 1.5 Add `TrySnapshotAsync` and `TryRestoreAsync` (design D1):
  - The snapshot copies the formats in enumeration order, with D1's filter: registered formats except OLE's `DataObject` and `Ole Private Data`, and standard formats with plain `HGLOBAL` data. It skips `CF_TEXT` and `CF_OEMTEXT` next to `CF_UNICODETEXT`, GDI handles, `CF_METAFILEPICT` and `CF_DSPMETAFILEPICT`, `CF_OWNERDISPLAY`, and the private and GDI-object ranges. A format that can't be read is skipped.
  - The restore writes the formats back in the same order, with the two history formats as DWORD `0` and `Pisum.Transcribe.Restored`.
  - The snapshot reports sensitive content from the exclusion formats as today.

  Verify: these cases pass against the new service:
  - `TryRestoreAsync_SnapshotOfText_RoundTripsTextExactly`
  - `TryRestoreAsync_Snapshot_SetsHistoryFormatsAndRestoredMarkerWithoutMonitorExclusion`
  - `TrySnapshotAsync_ContentWithOneExclusionFormat_ReportsSensitive`
  - `TrySnapshotAsync_PlainText_ReportsNotSensitive`
  - `TrySnapshotAsync_RestoredContent_ReportsNotSensitive`
  - a new `TryRestoreAsync_SnapshotOfHtmlImageAndFileList_RoundTripsEachFormat`, which puts HTML, a `CF_DIB` image and a `CF_HDROP` file list on the clipboard, then restores them and compares the bytes

## 2. Switch and remove

- [x] 2.1 Register `Win32ClipboardService` in `TextInsertionServiceCollectionExtensions`, and delete `WpfClipboardService` with its private snapshot subclass. Verify:
  - `TextInserterHardwareTests` pass.
  - Checked by hand: after a dictation into Notepad, a paste gives back the previous clipboard for text, for a file copied in Explorer, and for text or cells copied in Word or Excel. The last two go through OLE (design, Risks).

## 3. The test window

- [x] 3.1 Move `TestWindow` from WPF to the app's generated `PInvoke` (design D2):
  - Add `TranslateMessage`, `SetForegroundWindow`, `SetFocus`, `GetWindowText`, `GetWindowTextLength` and `SetWindowText` to `NativeMethods.txt` under a `// Test helpers` comment.
  - `TestWindow` becomes a top-level multiline `EDIT` window on its own thread, with a `GetMessage`, `TranslateMessage`, `DispatchMessage` loop.

  Verify:
  - `grep -rn "System.Windows" tests/Pisum.Transcribe.Tests/TextInsertion` finds nothing.
  - `TextInserterHardwareTests` and `ForegroundWindowTrackerHardwareTests` pass.

## 4. Documentation

- [x] 4.1 Update `CLAUDE.md`:
  - *Tests*: the clipboard no longer needs an STA thread, and `TextInsertion/TestWindow` is a Win32 window.
  - *Win32*: test helpers call the app's generated `PInvoke`, and functions that only the tests need go under `// Test helpers` in `NativeMethods.txt`.

  Verify: the STA bullet names only WPF windows and the tray icon, and the Win32 bullet names the test helpers.
