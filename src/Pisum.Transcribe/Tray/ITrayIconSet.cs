namespace Pisum.Transcribe.Tray;

/// <summary>
/// The platform's icons for the tray: one per <see cref="TrayStatus"/>, and the one before the first status.
/// </summary>
internal interface ITrayIconSet
{
    /// <summary>
    /// Raised on any thread when the icons changed, so the shown icon has to be reloaded.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// The icon before the first status is set.
    /// </summary>
    TrayIconImage Initial { get; }

    /// <summary>
    /// The icon of a status. It may be called from any thread.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The icon.</returns>
    TrayIconImage For(TrayStatus status);
}
