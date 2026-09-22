## 1. The Win32 clipboard service

- [ ] 1.1 Add the clipboard, global-memory and window functions to `NativeMethods.txt`: `OpenClipboard`, `CloseClipboard`, `EmptyClipboard`, `EnumClipboardFormats`, `GetClipboardData`, `SetClipboardData`, `RegisterClipboardFormat`, `GlobalAlloc`, `GlobalLock`, `GlobalUnlock`, `GlobalSize`, `GlobalFree`, `RegisterClassEx`, `CreateWindowEx`, `DestroyWindow`, `GetMessage`, `DispatchMessage` and `PostThreadMessage`, plus `HWND_MESSAGE`. Verify: `dotnet build Pisum.Transcribe.slnx` passes and CsWin32 generates them in `Windows.Win32.PInvoke`.
- [ ] 1.2 Add `TextInsertion/Windows/Win32ClipboardService : IClipboardService, IDisposable` (design D1). It lives in `TextInsertion/` if `extract-ui-seams` isn't in yet. It has:
  - its own thread with a message-only window as the clipboard owner, and a work queue with `TaskCompletionSource` results
  - `SequenceNumber` from `GetClipboardSequenceNumber`
  - `OpenClipboard` retried 10 times, 100 ms apart
  - `TrySetTextAsync` writing `CF_UNICODETEXT`, plus the three history formats when exclusion is asked for

  Verify: `Win32ClipboardServiceHardwareTests`, the renamed `WpfClipboardServiceHardwareTests`, pass these cases against the new service in `DesktopCollection`:
  - `TrySetTextAsync_ExcludeFromHistory_SetsTextAndExclusionFormats`
  - `TrySetTextAsync_NotExcludedFromHistory_SetsTextWithoutExclusionFormats`
  - `TrySetTextAsync_ClipboardHeldOpenByAnotherThread_ReturnsFalseAfterAboutOneSecond`
- [ ] 1.3 Add `TrySnapshotAsync` and `TryRestoreAsync` (design D1):
  - The snapshot copies every `HGLOBAL` format, skipping synthesized formats and GDI handles.
  - The restore writes the formats back with `Pisum.Transcribe.Restored`.
  - The snapshot reports sensitive content from the exclusion formats as today.

  Verify: these cases pass against the new service:
  - `TryRestoreAsync_SnapshotOfText_RoundTripsTextExactly`
  - `TryRestoreAsync_Snapshot_SetsHistoryFormatsAndRestoredMarkerWithoutMonitorExclusion`
  - `TrySnapshotAsync_ContentWithOneExclusionFormat_ReportsSensitive`
  - `TrySnapshotAsync_PlainText_ReportsNotSensitive`
  - `TrySnapshotAsync_RestoredContent_ReportsNotSensitive`
  - a new `TryRestoreAsync_SnapshotOfHtmlImageAndFileList_RoundTripsEachFormat`, which puts HTML, a `CF_DIB` image and a `CF_HDROP` file list on the clipboard, then restores them and compares the bytes

## 2. Switch and remove

- [ ] 2.1 Register `Win32ClipboardService` in `TextInsertionServiceCollectionExtensions`, and delete `WpfClipboardService`. Verify: `TextInserterHardwareTests` pass, and a dictation into Notepad restores the previous clipboard, checked by hand.

## 3. Test helpers

- [ ] 3.1 Move `RawClipboard` and `TestWindow` from WPF to CsWin32 (design D2). `TestWindow` becomes a plain Win32 top-level window with an edit control, on its own thread. Verify:
  - `grep -rn "System.Windows" tests/Pisum.Transcribe.Tests/TextInsertion` finds nothing.
  - `TextInserterHardwareTests` and `ForegroundWindowTrackerHardwareTests` pass.

## 4. Documentation

- [ ] 4.1 Update `CLAUDE.md` *Tests*: the clipboard no longer needs an STA thread, and `TextInsertion/TestWindow` is a Win32 window. Verify: the STA bullet names only WPF windows and the tray icon.
