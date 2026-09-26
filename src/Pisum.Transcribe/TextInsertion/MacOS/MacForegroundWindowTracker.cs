using Avalonia;

namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// The focused window of the focused application as the insertion target (design D1 of add-macos-text-insertion). The
/// tracker keeps the window element of the latest capture, and the target carries only a capture number. It also keeps
/// the window's frame from the capture, for the recording overlay's placement (design D3 of add-macos-dictation).
/// </summary>
/// <remarks>
/// Dictations never overlap, so every capture comes after the previous insertion, and the latest element is the only
/// one still needed. A capture releases the element of the one before.
/// </remarks>
internal sealed class MacForegroundWindowTracker : IForegroundWindowTracker, IDisposable
{
    private readonly IFocusedWindowReader _reader;
    private readonly Lock _gate = new();
    private long _captureNumber;
    private nint _window;
    private int _processId;
    private Rect? _frame;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="reader">The focused window reader.</param>
    public MacForegroundWindowTracker(IFocusedWindowReader reader)
    {
        _reader = reader;
    }

    /// <inheritdoc />
    public InsertionTarget CaptureForeground()
    {
        var found = _reader.TryRead(out var processId, out var window);

        // Read here, on the dictation's thread, so the overlay never waits for the target application on the UI thread.
        Rect? frame = found && _reader.TryReadFrame(window, out var readFrame) ? readFrame : null;
        lock (_gate)
        {
            ReleaseWindow();
            if (!found)
            {
                return new InsertionTarget(0, 0, false);
            }

            _window = window;
            _processId = processId;
            _frame = frame;
            _captureNumber++;
            return new InsertionTarget((nint) _captureNumber, processId, false);
        }
    }

    /// <inheritdoc />
    public bool IsForeground(InsertionTarget target)
    {
        lock (_gate)
        {
            if (target.Window == 0 || target.Window != _captureNumber || _window == 0 ||
                target.ProcessId != _processId)
            {
                return false;
            }

            if (!_reader.TryRead(out var processId, out var current))
            {
                return false;
            }

            try
            {
                return processId == _processId && _reader.AreSameWindow(_window, current);
            }
            finally
            {
                _reader.Release(current);
            }
        }
    }

    /// <summary>
    /// Returns the frame that the latest capture read, in global points with the origin at the top-left of the primary
    /// screen. Readable from any thread.
    /// </summary>
    /// <param name="captureNumber">The capture's window value from <see cref="InsertionTarget.Window"/>.</param>
    /// <param name="frame">The frame.</param>
    /// <returns>
    /// <see langword="false"/> for an older capture, a capture without a window, or a frame that could not be read.
    /// </returns>
    public bool TryGetFrame(nint captureNumber, out Rect frame)
    {
        lock (_gate)
        {
            if (captureNumber == 0 || captureNumber != _captureNumber || _frame is not { } captured)
            {
                frame = default;
                return false;
            }

            frame = captured;
            return true;
        }
    }

    /// <summary>
    /// Releases the element of the latest capture.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            ReleaseWindow();
        }
    }

    private void ReleaseWindow()
    {
        if (_window != 0)
        {
            _reader.Release(_window);
            _window = 0;
        }

        _frame = null;
    }
}
