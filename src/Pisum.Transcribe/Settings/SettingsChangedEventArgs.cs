namespace Pisum.Transcribe.Settings;

/// <summary>
/// The settings before and after a save.
/// </summary>
/// <param name="previous">The settings that were in effect before the save.</param>
/// <param name="current">The saved settings, now in effect.</param>
internal sealed class SettingsChangedEventArgs(AppSettings previous, AppSettings current) : EventArgs
{
    /// <summary>
    /// The settings that were in effect before the save.
    /// </summary>
    public AppSettings Previous { get; } = previous;

    /// <summary>
    /// The saved settings, now in effect.
    /// </summary>
    public AppSettings Current { get; } = current;
}
