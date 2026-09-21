namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// Reads the foreground window, to capture the insertion target and check it before the keystrokes.
/// </summary>
internal interface IForegroundWindowTracker
{
    /// <summary>
    /// Captures the current foreground window with its process and elevation.
    /// </summary>
    /// <returns>The target, with window handle 0 when no window is in the foreground.</returns>
    InsertionTarget CaptureForeground();

    /// <summary>
    /// Checks whether the target's window is the current foreground window.
    /// </summary>
    /// <param name="target">The target to check.</param>
    /// <returns><see langword="true"/> if the window handles match.</returns>
    bool IsForeground(InsertionTarget target);
}
