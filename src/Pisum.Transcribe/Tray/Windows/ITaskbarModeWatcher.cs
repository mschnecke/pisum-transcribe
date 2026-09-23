namespace Pisum.Transcribe.Tray;

/// <summary>
/// The taskbar's light or dark mode, and when it changes.
/// </summary>
internal interface ITaskbarModeWatcher
{
    /// <summary>
    /// Raised on a background thread when the mode flips.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// The current mode. It may be read from any thread.
    /// </summary>
    TaskbarMode Current { get; }
}
