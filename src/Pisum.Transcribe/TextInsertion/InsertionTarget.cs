namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// The window a transcript is inserted into, captured when the recording starts.
/// </summary>
/// <param name="Window">
/// A token for the window that only the platform's <see cref="IForegroundWindowTracker"/> understands: the window
/// handle on Windows, a capture number on macOS. 0 when no window was in the foreground.
/// </param>
/// <param name="ProcessId">The identifier of the process that owns the window, or 0 without a window.</param>
/// <param name="IsElevated">
/// Whether that process runs elevated. <see langword="true"/> when its token could not be read, the safe side. Always
/// <see langword="false"/> on macOS, which has no elevated windows.
/// </param>
internal sealed record InsertionTarget(nint Window, int ProcessId, bool IsElevated);
