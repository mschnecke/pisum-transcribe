using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// The general pasteboard through the Swift helper, on a dedicated pasteboard thread (design D2 of
/// add-macos-text-insertion). The UI thread never touches the pasteboard, and if a read ever alerts, it blocks only
/// that thread.
/// </summary>
/// <remarks>
/// A snapshot reads the pasteboard only while the system allows it without asking the user. Otherwise it returns
/// <see langword="null"/>, and the paste types the text instead. Setting and restoring only write, which never alerts.
/// Nothing throws for a pasteboard failure: the call returns <see langword="false"/> or <see langword="null"/>. Only
/// calls after <see cref="Dispose"/> fail with an <see cref="ObjectDisposedException"/>.
/// </remarks>
internal sealed class MacClipboardService : IClipboardService, IDisposable
{
    /// <summary>
    /// The nspasteboard.org marker of content that clipboard managers should ignore.
    /// </summary>
    public const string TransientType = "org.nspasteboard.TransientType";

    /// <summary>
    /// The nspasteboard.org marker of content such as passwords that clipboard managers should not show or keep.
    /// </summary>
    public const string ConcealedType = "org.nspasteboard.ConcealedType";

    /// <summary>
    /// Marks content that a restore put back, so that its transient marker doesn't make the next snapshot sensitive.
    /// </summary>
    public const string RestoredType = "io.github.mschnecke.pisum-transcribe.restored";

    private readonly IMacPasteboard _pasteboard;
    private readonly bool _isAvailable;
    private readonly ILogger<MacClipboardService> _logger;
    private readonly Channel<Action> _work =
        Channel.CreateUnbounded<Action>(new UnboundedChannelOptions {SingleReader = true});

    /// <summary>
    /// Initializes a new instance on the general pasteboard and starts the pasteboard thread.
    /// </summary>
    /// <param name="library">The helper, whose functions are called only while it is available.</param>
    /// <param name="logger">The logger.</param>
    public MacClipboardService(MacNativeLibrary library, ILogger<MacClipboardService> logger)
        : this(new MacPasteboard(null), library.IsAvailable, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance and starts the pasteboard thread, for tests.
    /// </summary>
    /// <param name="pasteboard">The pasteboard.</param>
    /// <param name="isAvailable">Whether the helper is available.</param>
    /// <param name="logger">The logger.</param>
    public MacClipboardService(IMacPasteboard pasteboard, bool isAvailable, ILogger<MacClipboardService> logger)
    {
        _pasteboard = pasteboard;
        _isAvailable = isAvailable;
        _logger = logger;
        new Thread(Run) {IsBackground = true, Name = "Pasteboard"}.Start();
    }

    /// <inheritdoc />
    public long SequenceNumber => _isAvailable ? _pasteboard.ChangeCount : 0;

    /// <inheritdoc />
    public Task<ClipboardSnapshot?> TrySnapshotAsync()
    {
        return InvokeAsync<ClipboardSnapshot?>(null, Snapshot);
    }

    /// <inheritdoc />
    public Task<bool> TrySetTextAsync(string text, bool excludeFromHistory)
    {
        return InvokeAsync(false, () =>
        {
            if (_pasteboard.SetText(text, excludeFromHistory))
            {
                return true;
            }

            _logger.LogWarning("The text could not be placed on the pasteboard");
            return false;
        });
    }

    /// <inheritdoc />
    public Task<bool> TryRestoreAsync(ClipboardSnapshot snapshot)
    {
        // Every snapshot this service hands out is its own; anything else is a programming error.
        var mac = (MacClipboardSnapshot) snapshot;
        return InvokeAsync(false, () =>
        {
            if (_pasteboard.Restore(MacPasteboardFormat.Write(mac.Items)))
            {
                return true;
            }

            _logger.LogWarning("The pasteboard could not be restored");
            return false;
        });
    }

    /// <summary>
    /// Ends the pasteboard thread after the work already queued. Later calls fail with an
    /// <see cref="ObjectDisposedException"/>.
    /// </summary>
    public void Dispose()
    {
        _work.Writer.TryComplete();
    }

    /// <summary>
    /// Whether an item carries a marker of the given type.
    /// </summary>
    private static bool Has(IReadOnlyList<MacPasteboardEntry> item, string type)
    {
        return item.Any(entry => entry.Type == type);
    }

    private ClipboardSnapshot? Snapshot()
    {
        // In the default and ask states a read alerts and blocks until the user answers (spike M5 of add-macos-shell).
        var accessBehavior = _pasteboard.AccessBehavior;
        if (accessBehavior is not (-1 or 2))
        {
            _logger.LogInformation(
                "The pasteboard may not be read without asking the user (access behavior {Behavior})",
                accessBehavior);
            return null;
        }

        var buffer = _pasteboard.Snapshot();
        var items = buffer is null ? null : MacPasteboardFormat.Read(buffer);
        if (items is null)
        {
            _logger.LogWarning("The pasteboard could not be copied");
            return null;
        }

        var isSensitive = items.Any(item => Has(item, ConcealedType) || Has(item, TransientType)) &&
                          !items.Any(item => Has(item, RestoredType));
        return new MacClipboardSnapshot(items, isSensitive);
    }

    /// <summary>
    /// Runs the action on the pasteboard thread, or returns <paramref name="unavailable"/> when the helper isn't
    /// available.
    /// </summary>
    private Task<T> InvokeAsync<T>(T unavailable, Func<T> action)
    {
        if (!_isAvailable)
        {
            _logger.LogWarning("The helper libPisumMac isn't available, so the pasteboard can't be used");
            return Task.FromResult(unavailable);
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = _work.Writer.TryWrite(() =>
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
        if (!queued)
        {
            completion.SetException(new ObjectDisposedException(nameof(MacClipboardService)));
        }

        return completion.Task;
    }

    private void Run()
    {
        var reader = _work.Reader;
        while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
        {
            while (reader.TryRead(out var work))
            {
                work();
            }
        }
    }
}
