namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// The focused window of the focused application as the insertion target (design D1 of add-macos-text-insertion). The
/// tracker keeps the window element of the latest capture, and the target carries only a capture number.
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
        lock (_gate)
        {
            ReleaseWindow();
            if (!found)
            {
                return new InsertionTarget(0, 0, false);
            }

            _window = window;
            _processId = processId;
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
    }
}
