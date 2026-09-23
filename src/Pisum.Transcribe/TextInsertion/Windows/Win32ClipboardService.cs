using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.WindowsAndMessaging;
using Microsoft.Extensions.Logging;

namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// The clipboard through the Win32 API, on a dedicated thread whose message-only window owns the clipboard.
/// </summary>
/// <remarks>
/// A Win32 failure never throws: the call returns <see langword="false"/> or <see langword="null"/>, so a paste falls
/// back to typing instead of failing the dictation. Only calls after <see cref="Dispose"/> fail with an
/// <see cref="ObjectDisposedException"/>.
/// </remarks>
internal sealed class Win32ClipboardService : IClipboardService, IDisposable
{
    /// <summary>
    /// Keeps content out of clipboard monitoring, including clipboard history. Its presence is enough.
    /// </summary>
    public const string ExcludeFromMonitoringFormat = "ExcludeClipboardContentFromMonitorProcessing";

    /// <summary>
    /// A DWORD; 0 keeps content out of clipboard history.
    /// </summary>
    public const string HistoryFormat = "CanIncludeInClipboardHistory";

    /// <summary>
    /// A DWORD; 0 keeps content out of the cloud clipboard.
    /// </summary>
    public const string CloudFormat = "CanUploadToCloudClipboard";

    /// <summary>
    /// Marks content put back by a restore, so that its history formats do not make the next snapshot sensitive.
    /// </summary>
    public const string RestoredFormat = "Pisum.Transcribe.Restored";

    /// <summary>
    /// The first format ID that <see cref="PInvoke.RegisterClipboardFormat(string)"/> hands out. Everything below is a
    /// standard format of <see cref="CLIPBOARD_FORMAT"/>.
    /// </summary>
    private const uint FirstRegisteredFormat = 0xC000;

    private const int OpenAttempts = 10;

    private static readonly TimeSpan OpenRetryDelay = TimeSpan.FromMilliseconds(100);
    private static readonly byte[] DwordZero = [0, 0, 0, 0];

    /// <summary>
    /// The content of a format whose presence is what counts. Not empty, because a zero-length format cannot be put on
    /// the clipboard.
    /// </summary>
    private static readonly byte[] Marker = [0];

    private readonly ILogger<Win32ClipboardService> _logger;
    private readonly Lock _gate = new();
    private readonly Queue<Work> _queue = new();
    private readonly uint _threadId;
    private readonly uint _excludeFromMonitoring;
    private readonly uint _history;
    private readonly uint _cloud;
    private readonly uint _restored;
    private readonly uint _oleDataObject;
    private readonly uint _olePrivateData;

    private HWND _window;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance and starts the clipboard thread.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public Win32ClipboardService(ILogger<Win32ClipboardService> logger)
    {
        _logger = logger;
        _excludeFromMonitoring = PInvoke.RegisterClipboardFormat(ExcludeFromMonitoringFormat);
        _history = PInvoke.RegisterClipboardFormat(HistoryFormat);
        _cloud = PInvoke.RegisterClipboardFormat(CloudFormat);
        _restored = PInvoke.RegisterClipboardFormat(RestoredFormat);
        _oleDataObject = PInvoke.RegisterClipboardFormat("DataObject");
        _olePrivateData = PInvoke.RegisterClipboardFormat("Ole Private Data");

        var started = new TaskCompletionSource<uint>();
        var thread = new Thread(() => Run(started)) {IsBackground = true, Name = "Clipboard"};
        thread.Start();
        _threadId = started.Task.GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public uint SequenceNumber => PInvoke.GetClipboardSequenceNumber();

    /// <inheritdoc />
    public Task<ClipboardSnapshot?> TrySnapshotAsync()
    {
        return InvokeAsync(Snapshot);
    }

    /// <inheritdoc />
    public Task<bool> TrySetTextAsync(string text, bool excludeFromHistory)
    {
        return InvokeAsync(() =>
        {
            var formats = new List<(uint Format, byte[] Bytes)>
            {
                ((uint) CLIPBOARD_FORMAT.CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + '\0')),
            };
            if (excludeFromHistory)
            {
                formats.Add((_excludeFromMonitoring, Marker));
                formats.Add((_history, DwordZero));
                formats.Add((_cloud, DwordZero));
            }

            return TryWrite(formats, "set the text");
        });
    }

    /// <inheritdoc />
    public Task<bool> TryRestoreAsync(ClipboardSnapshot snapshot)
    {
        // Every snapshot this service hands out is its own; anything else is a programming error.
        var win32 = (Win32ClipboardSnapshot) snapshot;
        return InvokeAsync(() =>
        {
            if (win32.Formats.Count == 0)
            {
                return TryWrite([], "clear the clipboard");
            }

            var formats = new List<(uint Format, byte[] Bytes)>(win32.Formats);

            // Already in the history from when the user copied it. Not excluded from monitoring, so clipboard tools
            // still see it.
            formats.Add((_history, DwordZero));
            formats.Add((_cloud, DwordZero));
            formats.Add((_restored, Marker));
            return TryWrite(formats, "restore the clipboard");
        });
    }

    /// <summary>
    /// Shuts down the clipboard thread. Calls that were still queued fail with an <see cref="ObjectDisposedException"/>.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
    }

    private static bool IsNonZeroDword(byte[]? bytes)
    {
        return bytes is {Length: >= sizeof(int)} && BitConverter.ToInt32(bytes) != 0;
    }

    /// <summary>
    /// Creates the message-only window that owns the clipboard. <c>SetClipboardData</c> fails after
    /// <see cref="PInvoke.EmptyClipboard"/> when <see cref="PInvoke.OpenClipboard"/> was given no window.
    /// </summary>
    /// <remarks>
    /// The built-in <c>STATIC</c> class already answers what an owner receives, such as <c>WM_DESTROYCLIPBOARD</c>, so
    /// the service needs no window class and no window procedure of its own.
    /// </remarks>
    private static unsafe HWND CreateOwnerWindow()
    {
        return PInvoke.CreateWindowEx(default, "STATIC", null, default, 0, 0, 0, 0, HWND.HWND_MESSAGE);
    }

    /// <summary>
    /// Reads the bytes of a clipboard format. The clipboard has to be open.
    /// </summary>
    /// <returns>The bytes, or <see langword="null"/> if the format could not be read.</returns>
    private static unsafe byte[]? ReadBytes(uint format)
    {
        var handle = PInvoke.GetClipboardData(format);
        if (handle.IsNull)
        {
            return null;
        }

        var memory = (HGLOBAL) (nint) handle;
        var size = (int) PInvoke.GlobalSize(memory);
        var pointer = size == 0 ? null : PInvoke.GlobalLock(memory);
        if (pointer is null)
        {
            return null;
        }

        try
        {
            return new ReadOnlySpan<byte>(pointer, size).ToArray();
        }
        finally
        {
            PInvoke.GlobalUnlock(memory);
        }
    }

    /// <summary>
    /// Puts one format on the open, emptied clipboard. After a successful
    /// <c>SetClipboardData</c> the system owns the memory, otherwise this method frees it.
    /// </summary>
    private static bool TryPut(uint format, byte[] bytes)
    {
        var memory = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, (nuint) bytes.Length);
        if (memory.IsNull)
        {
            return false;
        }

        if (TryFill(memory, bytes) && !PInvoke.SetClipboardData(format, (HANDLE) (nint) memory).IsNull)
        {
            return true;
        }

        // Kept across the free, so that the caller logs why the write failed.
        var error = Marshal.GetLastPInvokeError();
        PInvoke.GlobalFree(memory);
        Marshal.SetLastPInvokeError(error);
        return false;
    }

    private static unsafe bool TryFill(HGLOBAL memory, byte[] bytes)
    {
        var pointer = PInvoke.GlobalLock(memory);
        if (pointer is null)
        {
            return false;
        }

        bytes.CopyTo(new Span<byte>(pointer, bytes.Length));
        PInvoke.GlobalUnlock(memory);
        return true;
    }

    /// <summary>
    /// Waits while handling messages other threads send to the owner window. The process that holds the clipboard open
    /// may be the one that sends <c>WM_DESTROYCLIPBOARD</c>, and a plain sleep would stall its copy.
    /// </summary>
    private static void WaitHandlingSentMessages(TimeSpan duration)
    {
        var deadline = Environment.TickCount64 + (long) duration.TotalMilliseconds;
        for (var remaining = (long) duration.TotalMilliseconds;
             remaining > 0;
             remaining = deadline - Environment.TickCount64)
        {
            PInvoke.MsgWaitForMultipleObjectsEx(default, (uint) remaining, QUEUE_STATUS_FLAGS.QS_SENDMESSAGE, default);

            // Dispatches the sent messages that ended the wait. Posted messages, such as the work queue's, stay queued.
            PInvoke.PeekMessage(out _, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
        }
    }

    /// <summary>
    /// The clipboard thread: it owns the window, runs the queued calls, and cleans up after <c>WM_QUIT</c>.
    /// </summary>
    private void Run(TaskCompletionSource<uint> started)
    {
        _window = CreateOwnerWindow();
        if (_window.IsNull)
        {
            started.SetException(new InvalidOperationException(
                $"The clipboard window could not be created (Win32 error {Marshal.GetLastPInvokeError()})."));
            return;
        }

        // The window gave the thread its message queue, so every later post arrives.
        started.SetResult(PInvoke.GetCurrentThreadId());

        while (PInvoke.GetMessage(out var message, default, 0, 0))
        {
            if (message.hwnd.IsNull && message.message == PInvoke.WM_APP)
            {
                RunQueued();
            }
            else
            {
                PInvoke.DispatchMessage(message);
            }
        }

        PInvoke.DestroyWindow(_window);
        RunQueued();
    }

    private void RunQueued()
    {
        while (true)
        {
            Work work;
            bool disposed;
            lock (_gate)
            {
                if (!_queue.TryDequeue(out var next))
                {
                    return;
                }

                work = next;
                disposed = _disposed;
            }

            work(disposed);
        }
    }

    /// <summary>
    /// Runs a clipboard call on the clipboard thread. Callers resume on the thread pool, never on the clipboard thread.
    /// </summary>
    private Task<T> InvokeAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_disposed)
            {
                completion.SetException(new ObjectDisposedException(nameof(Win32ClipboardService)));
                return completion.Task;
            }

            _queue.Enqueue(disposed =>
            {
                if (disposed)
                {
                    completion.SetException(new ObjectDisposedException(nameof(Win32ClipboardService)));
                    return;
                }

                try
                {
                    completion.SetResult(action());
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            });
        }

        PInvoke.PostThreadMessage(_threadId, PInvoke.WM_APP, default, default);
        return completion.Task;
    }

    /// <summary>
    /// Opens the clipboard, retrying a busy one for about a second.
    /// </summary>
    private bool TryOpen(string action)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (PInvoke.OpenClipboard(_window))
            {
                return true;
            }

            if (attempt == OpenAttempts)
            {
                _logger.LogWarning("The clipboard was busy, could not {Action} (Win32 error {Error})", action,
                    Marshal.GetLastPInvokeError());
                return false;
            }

            WaitHandlingSentMessages(OpenRetryDelay);
        }
    }

    /// <summary>
    /// Replaces the clipboard contents with the given formats, in their order. An empty list clears the clipboard.
    /// </summary>
    private bool TryWrite(IReadOnlyList<(uint Format, byte[] Bytes)> formats, string action)
    {
        if (!TryOpen(action))
        {
            return false;
        }

        try
        {
            if (!PInvoke.EmptyClipboard())
            {
                LogFailure(action);
                return false;
            }

            foreach (var (format, bytes) in formats)
            {
                if (TryPut(format, bytes))
                {
                    continue;
                }

                LogFailure(action);

                // Without its exclusion formats a transcript would reach clipboard history, so leave nothing behind.
                PInvoke.EmptyClipboard();
                return false;
            }

            return true;
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    private void LogFailure(string action)
    {
        _logger.LogWarning("Could not {Action} (Win32 error {Error})", action, Marshal.GetLastPInvokeError());
    }

    private ClipboardSnapshot? Snapshot()
    {
        if (!TryOpen("read the clipboard"))
        {
            return null;
        }

        try
        {
            var ids = new List<uint>();
            for (var format = PInvoke.EnumClipboardFormats(0); format != 0;
                 format = PInvoke.EnumClipboardFormats(format))
            {
                ids.Add(format);
            }

            var hasUnicodeText = ids.Contains((uint) CLIPBOARD_FORMAT.CF_UNICODETEXT);
            var copy = new List<(uint Format, byte[] Bytes)>(ids.Count);
            var excluded = false;
            var restored = false;
            foreach (var format in ids)
            {
                if (format == _excludeFromMonitoring)
                {
                    excluded = true;
                }
                else if (format == _history || format == _cloud)
                {
                    // An unreadable value counts as 0, the safe side.
                    excluded |= !IsNonZeroDword(ReadBytes(format));
                }
                else if (format == _restored)
                {
                    restored = true;
                }
                else if (IsCopyable(format, hasUnicodeText))
                {
                    var bytes = ReadBytes(format);
                    if (bytes is null)
                    {
                        // Best effort: a delay-rendered or non-HGLOBAL format is not restored.
                        _logger.LogDebug("The clipboard format {Format} could not be read, it is not restored", format);
                    }
                    else
                    {
                        copy.Add((format, bytes));
                    }
                }
            }

            // Only content that was not sensitive is ever restored, so the marker never hides a password.
            return new Win32ClipboardSnapshot(copy, excluded && !restored);
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    /// <summary>
    /// Whether a format goes into a snapshot. Copied are the registered formats and the standard formats whose data is
    /// plain global memory, because only those survive being written back from bytes.
    /// </summary>
    private bool IsCopyable(uint format, bool hasUnicodeText)
    {
        if (format >= FirstRegisteredFormat)
        {
            // They describe the OLE data object of the process that copied. Put back without an OLE owner, they could
            // make the next application's OLE offer formats that are not there.
            return format != _oleDataObject && format != _olePrivateData;
        }

        // The private and GDI-object ranges, whose meaning only the source knows.
        if (format is >= (uint) CLIPBOARD_FORMAT.CF_PRIVATEFIRST and <= (uint) CLIPBOARD_FORMAT.CF_GDIOBJLAST)
        {
            return false;
        }

        return (CLIPBOARD_FORMAT) format switch
        {
            // Windows synthesizes them again from the restored Unicode text.
            CLIPBOARD_FORMAT.CF_TEXT or CLIPBOARD_FORMAT.CF_OEMTEXT => !hasUnicodeText,

            // GDI handles, not memory. Windows synthesizes CF_BITMAP again from CF_DIB.
            CLIPBOARD_FORMAT.CF_BITMAP or CLIPBOARD_FORMAT.CF_PALETTE or CLIPBOARD_FORMAT.CF_ENHMETAFILE
                or CLIPBOARD_FORMAT.CF_DSPBITMAP or CLIPBOARD_FORMAT.CF_DSPENHMETAFILE => false,

            // Global memory, but it holds an HMETAFILE that the source owns, so copied bytes would restore a handle
            // that is no longer valid.
            CLIPBOARD_FORMAT.CF_METAFILEPICT or CLIPBOARD_FORMAT.CF_DSPMETAFILEPICT => false,

            // It has no data.
            CLIPBOARD_FORMAT.CF_OWNERDISPLAY => false,

            _ => true,
        };
    }

    /// <summary>
    /// A queued clipboard call. The argument says whether the service was disposed before it ran, in which case the
    /// call fails instead.
    /// </summary>
    private delegate void Work(bool disposed);
}
