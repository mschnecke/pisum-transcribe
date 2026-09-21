using System.Runtime.InteropServices;
using System.Windows.Threading;
using Windows.Win32;
using Microsoft.Extensions.Logging;

namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// The clipboard through WPF's <see cref="Clipboard"/>, on a dedicated STA thread that runs its own
/// <see cref="Dispatcher"/>. The UI dispatcher is never used for the clipboard.
/// </summary>
/// <remarks>
/// WPF retries a busy clipboard 10 times with a 100 ms sleep on the calling thread and then throws a
/// <see cref="COMException"/>, which the service turns into a failed result. It has no retry loop of its own.
/// </remarks>
internal sealed class WpfClipboardService : IClipboardService, IDisposable
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

    private readonly Dispatcher _dispatcher;
    private readonly ILogger<WpfClipboardService> _logger;

    /// <summary>
    /// Initializes a new instance and starts the clipboard thread.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public WpfClipboardService(ILogger<WpfClipboardService> logger)
    {
        _logger = logger;

        var started = new TaskCompletionSource<Dispatcher>();
        var thread = new Thread(() =>
        {
            started.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }) {IsBackground = true, Name = "Clipboard"};
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        _dispatcher = started.Task.GetAwaiter().GetResult();
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
            var data = new DataObject(DataFormats.UnicodeText, text);
            if (excludeFromHistory)
            {
                data.SetData(ExcludeFromMonitoringFormat, Marker());
                data.SetData(HistoryFormat, Dword(0));
                data.SetData(CloudFormat, Dword(0));
            }

            return TrySet(data, "set the text");
        });
    }

    /// <inheritdoc />
    public Task<bool> TryRestoreAsync(ClipboardSnapshot snapshot)
    {
        return InvokeAsync(() =>
        {
            var formats = snapshot.Data.GetFormats(false);
            if (formats.Length == 0)
            {
                return TrySet(null, "clear the clipboard");
            }

            var data = new DataObject();
            foreach (var format in formats)
            {
                data.SetData(format, snapshot.Data.GetData(format, false), false);
            }

            // Already in the history from when the user copied it. Not excluded from monitoring, so clipboard tools
            // still see it.
            data.SetData(HistoryFormat, Dword(0));
            data.SetData(CloudFormat, Dword(0));
            data.SetData(RestoredFormat, Marker());
            return TrySet(data, "restore the clipboard");
        });
    }

    /// <summary>
    /// Shuts down the clipboard thread.
    /// </summary>
    public void Dispose()
    {
        _dispatcher.InvokeShutdown();
    }

    private static MemoryStream Dword(int value)
    {
        return new MemoryStream(BitConverter.GetBytes(value));
    }

    /// <summary>
    /// The content of a format whose presence is what counts. Not empty: WPF puts no zero-length data on the Windows
    /// clipboard, so an empty format would be visible only inside this process.
    /// </summary>
    private static MemoryStream Marker()
    {
        return new MemoryStream([0]);
    }

    private static bool IsNonZeroDword(object? data)
    {
        return data is MemoryStream {Length: >= sizeof(int)} stream && BitConverter.ToInt32(stream.ToArray()) != 0;
    }

    private ClipboardSnapshot? Snapshot()
    {
        IDataObject? current;
        string[] formats;
        try
        {
            current = Clipboard.GetDataObject();
            formats = current?.GetFormats(false) ?? [];
        }
        catch (ExternalException exception)
        {
            LogBusy(exception, "read the clipboard");
            return null;
        }

        var copy = new DataObject();
        var excluded = false;
        var restored = false;
        foreach (var format in formats)
        {
            object? data = null;
            try
            {
                data = current!.GetData(format, false);
            }
            catch (Exception exception)
            {
                // Best effort: delay-rendered and non-HGLOBAL formats may not survive.
                _logger.LogDebug(exception, "The clipboard format {Format} could not be read, it is not restored",
                    format);
            }

            switch (format)
            {
                case ExcludeFromMonitoringFormat:
                    excluded = true;
                    break;
                case HistoryFormat or CloudFormat:
                    // An unreadable value counts as 0, the safe side.
                    excluded |= !IsNonZeroDword(data);
                    break;
                case RestoredFormat:
                    restored = true;
                    break;
                default:
                    if (data is not null)
                    {
                        copy.SetData(format, data, false);
                    }

                    break;
            }
        }

        // Only content that was not sensitive is ever restored, so the marker never hides a password.
        return new ClipboardSnapshot(copy, excluded && !restored);
    }

    private bool TrySet(DataObject? data, string action)
    {
        try
        {
            if (data is null)
            {
                Clipboard.Clear();
            }
            else
            {
                Clipboard.SetDataObject(data, true);
            }

            return true;
        }
        catch (ExternalException exception)
        {
            LogBusy(exception, action);
            return false;
        }
    }

    private void LogBusy(ExternalException exception, string action)
    {
        _logger.LogWarning("The clipboard was busy, could not {Action} (HRESULT 0x{ErrorCode:X8})", action,
            exception.ErrorCode);
    }

    /// <summary>
    /// Runs a clipboard call on the clipboard thread. Callers resume on the thread pool, never on the clipboard thread.
    /// </summary>
    private Task<T> InvokeAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = _dispatcher.InvokeAsync(() =>
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });

        // The operation never runs once the clipboard thread has shut down.
        operation.Aborted += (_, _) =>
            completion.TrySetException(new ObjectDisposedException(nameof(WpfClipboardService)));
        if (operation.Status == DispatcherOperationStatus.Aborted)
        {
            completion.TrySetException(new ObjectDisposedException(nameof(WpfClipboardService)));
        }

        return completion.Task;
    }
}
