namespace Pisum.Transcribe.Tray;

/// <summary>
/// The key <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize</c>, which holds the taskbar's mode.
/// </summary>
internal interface IPersonalizeKey : IDisposable
{
    /// <summary>
    /// Reads the value <c>SystemUsesLightTheme</c>, the mode of the taskbar and Start.
    /// </summary>
    /// <returns>The value, or <see langword="null"/> if the key or the value does not exist.</returns>
    object? ReadSystemUsesLightTheme();

    /// <summary>
    /// Signals an event once, at the next change of a value in the key. Call it again after each signal.
    /// </summary>
    /// <param name="changed">The event to signal.</param>
    /// <returns><see langword="false"/> if the key can't be watched.</returns>
    bool NotifyOnChange(EventWaitHandle changed);
}
