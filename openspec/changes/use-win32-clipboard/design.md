## Context

See proposal.md, Why. It's one of the five Windows changes before the macOS port (see `extract-ui-seams`' design for the graph). It doesn't depend on the others.

Current state:
- **`WpfClipboardService`** runs WPF's OLE `Clipboard` on a dedicated STA thread that runs its own `Dispatcher`. The UI dispatcher is never used for the clipboard.
- **Busy clipboard:** WPF retries a busy clipboard 10 times with 100 ms sleeps, then throws a `COMException`, which the service turns into a failed result. That is where the specs' "about 1 second" comes from. The service has no retry loop of its own.
- **Formats** written for history exclusion:
  - `ExcludeClipboardContentFromMonitorProcessing`, where its presence is enough
  - `CanIncludeInClipboardHistory` and `CanUploadToCloudClipboard`, each a DWORD `0`
  - the app's own `Pisum.Transcribe.Restored`, which marks content put back by a restore
- **Snapshot** copies every format best-effort through `DataObject`. **Restore** puts it back with the restored marker. WPF reads the clipboard through OLE, whose list of formats hides OLE's own `DataObject` and `Ole Private Data`.
- **`ClipboardSnapshot`** holds that WPF `DataObject`. `TextInserter` only reads `IsSensitive` and hands the snapshot back to `TryRestoreAsync`. `TextInserterTests` build their snapshots from `DataObject`.
- **`SequenceNumber`** is `GetClipboardSequenceNumber`.
- **The tests:** `WpfClipboardServiceHardwareTests`, `RawClipboard` and `TestWindow` use WPF and STA threads. `RawClipboard` also has `DllImport`s of its own.

## Goals / Non-Goals

**Goals:** every `text-insertion` requirement holds unchanged. The hardware tests pass with the same cases.

**Non-Goals:** a macOS clipboard, or any change in what is snapshot, written or restored.

## Decisions

### D1: The Win32 clipboard API on a thread of its own

- **Thread and owner:**
  - The service runs on a dedicated thread. It creates a message-only window (parent `HWND_MESSAGE`) of the built-in `STATIC` class and pumps its messages. The window is the clipboard owner.
  - `SetClipboardData` fails after `EmptyClipboard` if `OpenClipboard` was given no window.
  - `STATIC`'s own window procedure handles what an owner receives, such as `WM_DESTROYCLIPBOARD`. So there's no `RegisterClassEx`, no managed window procedure to keep alive, and no `DefWindowProc`.
  - The thread reports that it has started only after the window exists, with its native thread ID from `GetCurrentThreadId`. Creating the window gives the thread its message queue, so every later post reaches it.
- **Work queue:**
  - Callers put the work and a `TaskCompletionSource` in a queue and wake the thread with `PostThreadMessage(WM_APP)`. When `GetMessage` returns that thread message, which has no window, the loop runs every queued item. It dispatches every other message.
  - The results complete with `RunContinuationsAsynchronously`, so callers resume on the thread pool, as they do today.
  - A thread message is lost only inside a modal loop, and this thread never runs one.
  - `Dispose` posts `WM_QUIT`. The loop ends, destroys the window, and fails the items still queued with `ObjectDisposedException`, as today.
- **Busy clipboard:**
  - `OpenClipboard` is retried 10 times, 100 ms apart. After that the service returns the failed result.
  - That keeps the "about 1 second" that WPF provided before.
- **Snapshot:**
  - Formats are copied in the order `EnumClipboardFormats` returns them, which is the order the source placed them, most descriptive first. Paste targets take the first format they understand, so the restore keeps that order.
  - Copied: registered formats (`0xC000` and above), and standard formats whose data is plain `HGLOBAL` memory, such as `CF_UNICODETEXT`, `CF_DIB`, `CF_DIBV5`, `CF_HDROP` and `CF_LOCALE`.
  - Skipped:
    - `CF_TEXT` and `CF_OEMTEXT` when `CF_UNICODETEXT` is there. Windows synthesizes them again from the restored Unicode text.
    - GDI handles: `CF_BITMAP`, `CF_PALETTE`, `CF_ENHMETAFILE`, `CF_DSPBITMAP` and `CF_DSPENHMETAFILE`. Windows synthesizes `CF_BITMAP` again from `CF_DIB`.
    - `CF_METAFILEPICT` and `CF_DSPMETAFILEPICT`. Their memory is `HGLOBAL`, but it holds an `HMETAFILE` that the source owns, so copied bytes would restore a handle that is no longer valid.
    - `CF_OWNERDISPLAY`, which has no data, and the private (`0x0200`–`0x02FF`) and GDI-object (`0x0300`–`0x03FF`) ranges.
    - OLE's `DataObject` and `Ole Private Data`, which describe the OLE data object of the process that copied. OLE's list of formats hid them, so WPF never copied them. Put back without an OLE owner, they could make the next application's OLE offer formats that aren't there.
  - The exclusion formats and the restored marker set `IsSensitive` as today and aren't copied.
  - That's best effort beyond text, as the interface promises.
- **Writing text:** `CF_UNICODETEXT`, plus the history formats with their DWORD values when exclusion is asked for, registered with `RegisterClipboardFormat` under the same names.
- **Restore:** it writes the snapshot's formats back in their order, plus `CanIncludeInClipboardHistory` and `CanUploadToCloudClipboard` as DWORD `0` and `Pisum.Transcribe.Restored`, as today. It doesn't write `ExcludeClipboardContentFromMonitorProcessing`, so clipboard tools still see the restored content.
- **The sequence number** stays `GetClipboardSequenceNumber`.

*Rejected:*
- **Avalonia's `IClipboard`:** it is asynchronous and text-oriented. It can't enumerate raw formats or write the history-exclusion formats.
- **The OLE clipboard with a hand-written `IDataObject`:** more code for the same result, and the owner rules still apply.
- **Keeping WPF's clipboard on its STA thread:** it would keep WPF in the build after the Avalonia shell.
- **A window class of its own with a managed window procedure:** more interop for nothing the owner needs.
- **The built-in `Message` class:** Windows documents it as reserved for the system.

### D2: Tests

- `WpfClipboardServiceHardwareTests` becomes `Win32ClipboardServiceHardwareTests`, with the same cases, in `DesktopCollection`.
- **Win32 calls:**
  - `RawClipboard`, the tests' independent view of the clipboard, and `TestWindow` call the app's generated `Windows.Win32.PInvoke`, which the test project sees through `InternalsVisibleTo`. The desktop tests then don't depend on the UI framework, which the Avalonia shell needs.
  - The service needs most of the functions anyway. The few that only the tests need (`TranslateMessage`, `SetForegroundWindow`, `SetFocus`, `GetWindowText`, `GetWindowTextLength` and `SetWindowText`) go into `NativeMethods.txt` under a `// Test helpers` comment.
- **`RawClipboard`** gets `SetText`, and a `Set` that puts raw formats with their bytes on the clipboard, next to `Read`, `GetText` and `Hold`.
- **`TestWindow`** is a top-level multiline `EDIT` window on its own thread:
  - `CreateWindowEx` with `WS_EX_TOPMOST` and `ES_MULTILINE`, then `SetForegroundWindow` and `SetFocus`. Windows may refuse the foreground, as it may today.
  - The loop is `GetMessage`, `TranslateMessage`, `DispatchMessage`. `TranslateMessage` turns Ctrl+V into the edit control's paste, and SharpHook's `VK_PACKET` input into characters.
  - A multiline edit control stores Enter as CR LF, as WPF's `TextBox` did, and keeps a surrogate pair that arrives as two characters.
  - `Text` reads with `GetWindowText`, `Clear` uses `SetWindowText`, and `Dispose` posts `WM_QUIT` to the thread, which destroys the window.
- **`TextInserterTests`** and **`TextInserterHardwareTests`** stop using `DataObject` (D3).

*Rejected:*
- **CsWin32 in the test project with its own `NativeMethods.txt`:** it would generate a second `Windows.Win32.PInvoke` next to the app's, which the tests also see. Whether CsWin32 0.3.333 avoids that conflict wasn't verified, and `TreatWarningsAsErrors` turns CS0436 into an error.
- **Keeping `DllImport` in the test helpers:** against the repository's rule for Win32 calls.
- **A top-level window of its own class with an edit child:** it needs a registered class, a managed window procedure, resizing, and passing the focus on to the child.

### D3: The snapshot type

- `ClipboardSnapshot` in `TextInsertion/` becomes `internal abstract record ClipboardSnapshot(bool IsSensitive)`.
- `Win32ClipboardSnapshot` in `TextInsertion/Windows/` derives from it and holds the copied formats as an ordered list of format ID and bytes. `TryRestoreAsync` casts to it; any other snapshot type there is a programming error.
- `TextInserter` doesn't change: it reads `IsSensitive` and hands the snapshot back.
- `TextInserterTests` use a small test subclass that holds a string, which stands for "the clipboard holds what was last set or restored".
- The macOS clipboard of `add-macos-text-insertion` adds its own subclass without changing shared code. A pasteboard holds several items with several types each, which a shared flat list wouldn't fit.
- While both services exist (Migration Plan, step 1), `WpfClipboardService` keeps its `DataObject` in a private subclass, which goes with it.

*Rejected:*
- **One shared record with a list of Win32 format IDs and bytes:** it puts Windows-only data in the shared folder, and the macOS port would have to reshape it.
- **A shared list keyed by format name:** Windows' standard formats have no names, and it still wouldn't fit a pasteboard's items.

## Risks / Trade-offs

- [A format that WPF's `DataObject` preserved is lost in a snapshot] → The hardware tests put text, HTML, an image and a file list on the clipboard and check the restore. A format that isn't `HGLOBAL` was best effort before too.
- [The Win32 snapshot sees formats that OLE's list hid, and restoring some of them could do harm] → D1 skips them explicitly. The hardware tests put formats on the clipboard with plain Win32 calls, so they never show OLE's formats. The manual check of task 2.1 copies through OLE instead: a file in Explorer, and text or cells in Word or Excel.
- [Global memory handling leaks or double-frees] → Every `GlobalAlloc` has one owner. After a successful `SetClipboardData` the system owns the memory, and otherwise the service frees it. That's covered by the restore tests, which run many times.
- [Another process holds the clipboard longer than 1 second] → Unchanged behavior: the busy-clipboard fallback of the spec.

## Migration Plan

1. The new service next to the old one, switched in the registration. Hardware tests on a desktop.
2. Remove `WpfClipboardService`.
3. No release of its own is needed.

**Rollback:** revert the pull request.
