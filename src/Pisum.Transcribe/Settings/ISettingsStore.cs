namespace Pisum.Transcribe.Settings;

/// <summary>
/// Loads and saves the user settings.
/// </summary>
internal interface ISettingsStore
{
    /// <summary>
    /// Raised after <see cref="SaveAsync"/> saved the settings, on the thread that saved them. Not raised when the
    /// save fails, or by <see cref="Load"/>.
    /// </summary>
    event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <summary>
    /// The settings in effect. Holds the defaults until <see cref="Load"/> runs.
    /// </summary>
    AppSettings Current { get; }

    /// <summary>
    /// Loads the settings file into <see cref="Current"/>. Runs at startup. A missing file gives the defaults, and a
    /// corrupt file is set aside as <c>settings.json.corrupt</c> and gives the defaults.
    /// </summary>
    void Load();

    /// <summary>
    /// Saves the settings and makes them <see cref="Current"/>. The settings file always holds either the previous or
    /// the new complete settings, even if the process ends during the save.
    /// </summary>
    /// <param name="settings">The settings to save.</param>
    /// <param name="cancellationToken">A token to cancel the save.</param>
    /// <returns>A task that completes when the settings are saved.</returns>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
