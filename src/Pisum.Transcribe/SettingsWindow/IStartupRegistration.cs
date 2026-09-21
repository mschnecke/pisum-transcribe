namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// Starts Pisum Transcribe when the current user signs in to Windows. The Windows startup entry is the only record of
/// the choice, so it also reflects changes made in Task Manager.
/// </summary>
internal interface IStartupRegistration
{
    /// <summary>
    /// Reads whether Windows starts Pisum Transcribe at sign-in: its startup entry exists, and Task Manager has not
    /// disabled it.
    /// </summary>
    /// <returns><see langword="true"/> if Windows starts the application at sign-in.</returns>
    bool IsEnabled();

    /// <summary>
    /// Turns starting at sign-in on or off for the current user. Turning it on also removes Task Manager's override,
    /// if the user disabled the entry there.
    /// </summary>
    /// <param name="enabled">Whether Windows starts the application at sign-in.</param>
    /// <exception cref="IOException">The registry could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The registry could not be written.</exception>
    /// <exception cref="System.Security.SecurityException">The registry could not be written.</exception>
    void SetEnabled(bool enabled);
}
