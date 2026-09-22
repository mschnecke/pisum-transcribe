## Context

See proposal.md, Why. It's one of the five Windows changes before the macOS port (see `extract-ui-seams`' design for the graph). It doesn't depend on the others.

Current state:
- **`WpfClipboardService`** runs WPF's OLE `Clipboard` on a dedicated STA thread that runs its own `Dispatcher`. The UI dispatcher is never used for the clipboard.
- **Busy clipboard:** WPF retries a busy clipboard 10 times with 100 ms sleeps, then throws a `COMException`, which the service turns into a failed result. That is where the specs' "about 1 second" comes from. The service has no retry loop of its own.
- **Formats** written for history exclusion:
  - `ExcludeClipboardContentFromMonitorProcessing`, where its presence is enough
  - `CanIncludeInClipboardHistory` and `CanUploadToCloudClipboard`, each a DWORD `0`
  - the app's own `Pisum.Transcribe.Restored`, which marks content put back by a restore
- **Snapshot** copies every format best-effort through `DataObject`. **Restore** puts it back with the restored marker.
- **`SequenceNumber`** is `GetClipboardSequenceNumber`.
- **The tests:** `WpfClipboardServiceHardwareTests`, `RawClipboard` and `TestWindow` use WPF and STA threads.

## Goals / Non-Goals

**Goals:** every `text-insertion` requirement holds unchanged. The hardware tests pass with the same cases.

**Non-Goals:** a macOS clipboard, or any change in what is snapshot, written or restored.

## Decisions

### D1: The Win32 clipboard API on a thread of its own

- **Thread and owner:**
  - The service runs on a dedicated thread that creates a message-only window (`HWND_MESSAGE`) and pumps its messages. The window is the clipboard owner.
  - `SetClipboardData` fails after `EmptyClipboard` if `OpenClipboard` was given no window.
  - Callers reach the thread through a small queue with `TaskCompletionSource` results, as they reach the WPF dispatcher today.
- **Busy clipboard:**
  - `OpenClipboard` is retried 10 times, 100 ms apart. After that the service returns the failed result.
  - That keeps the "about 1 second" that WPF provided before.
- **Snapshot:**
  - Every format from `EnumClipboardFormats` whose data is `HGLOBAL` memory is copied.
  - Formats Windows synthesizes (`CF_TEXT` and `CF_OEMTEXT` next to `CF_UNICODETEXT`, and `CF_BITMAP` next to `CF_DIB`) are skipped, and so are GDI handles.
  - That's best effort beyond text, as the interface promises.
- **Writing text:** `CF_UNICODETEXT`, plus the history formats with their DWORD values when exclusion is asked for, registered with `RegisterClipboardFormat` under the same names.
- **Restore:** it writes the snapshot's formats back, plus `Pisum.Transcribe.Restored`.
- **The sequence number** stays `GetClipboardSequenceNumber`.

*Rejected:*
- **Avalonia's `IClipboard`:** it is asynchronous and text-oriented. It can't enumerate raw formats or write the history-exclusion formats.
- **The OLE clipboard with a hand-written `IDataObject`:** more code for the same result, and the owner rules still apply.
- **Keeping WPF's clipboard on its STA thread:** it would keep WPF in the build after the Avalonia shell.

### D2: Tests

- `WpfClipboardServiceHardwareTests` becomes `Win32ClipboardServiceHardwareTests`, with the same cases, in `DesktopCollection`.
- `RawClipboard`, the tests' independent view of the clipboard, and `TestWindow` use CsWin32 instead of WPF. The desktop tests then don't depend on the UI framework, which the Avalonia shell needs.

## Risks / Trade-offs

- [A format that WPF's `DataObject` preserved is lost in a snapshot] → The hardware tests put text, HTML, an image and a file list on the clipboard and check the restore. A format that isn't `HGLOBAL` was best effort before too.
- [Global memory handling leaks or double-frees] → Every `GlobalAlloc` has one owner. After a successful `SetClipboardData` the system owns the memory, and otherwise the service frees it. That's covered by the restore tests, which run many times.
- [Another process holds the clipboard longer than 1 second] → Unchanged behavior: the busy-clipboard fallback of the spec.

## Migration Plan

1. The new service next to the old one, switched in the registration. Hardware tests on a desktop.
2. Remove `WpfClipboardService`.
3. No release of its own is needed.

**Rollback:** revert the pull request.
