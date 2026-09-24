using Windows.Win32;
using Microsoft.Extensions.Logging;

namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// Reads the foreground window with Win32 calls.
/// </summary>
internal sealed class ForegroundWindowTracker : IForegroundWindowTracker
{
    private readonly ILogger<ForegroundWindowTracker> _logger;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public ForegroundWindowTracker(ILogger<ForegroundWindowTracker> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public InsertionTarget CaptureForeground()
    {
        var window = PInvoke.GetForegroundWindow();
        if (window.IsNull)
        {
            return new InsertionTarget(0, 0, false);
        }

        PInvoke.GetWindowThreadProcessId(window, out var processId);
        var isElevated = ProcessElevation.IsElevated(processId, out var error);
        if (isElevated is null)
        {
            // Usually access denied for a process of higher integrity. Treated as elevated, the safe side.
            _logger.LogInformation(
                "The elevation of process {ProcessId} could not be read (Win32 error {Error}), treating it as elevated",
                processId, error);
        }

        return new InsertionTarget(window, (int) processId, isElevated ?? true);
    }

    /// <inheritdoc />
    public bool IsForeground(InsertionTarget target)
    {
        return PInvoke.GetForegroundWindow() == target.Window;
    }
}
