namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// The window a transcript is inserted into, captured when the recording starts.
/// </summary>
/// <param name="WindowHandle">The window handle, or 0 when no window was in the foreground.</param>
/// <param name="ProcessId">The identifier of the process that owns the window, or 0 without a window.</param>
/// <param name="IsElevated">
/// Whether that process runs elevated. <see langword="true"/> when its token could not be read, the safe side.
/// </param>
internal sealed record InsertionTarget(nint WindowHandle, int ProcessId, bool IsElevated);
