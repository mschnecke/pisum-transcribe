namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// Starts Pisum Transcribe when the current user signs in: "Start with Windows" through the Windows startup entry, and
/// "Open at login" through the macOS login item. The platform's record is the only record of the choice, so it also
/// reflects changes made in Task Manager or in System Settings.
/// </summary>
internal interface IStartupRegistration
{
    /// <summary>
    /// Reads whether the platform starts Pisum Transcribe at sign-in: on Windows its startup entry exists and Task
    /// Manager has not disabled it, and on macOS its login item is enabled.
    /// </summary>
    /// <returns><see langword="true"/> if the platform starts the application at sign-in.</returns>
    bool IsEnabled();

    /// <summary>
    /// Reads whether the platform needs the user's approval before it starts Pisum Transcribe at sign-in, as macOS does
    /// after the user turned the login item off in System Settings. Always <see langword="false"/> on Windows.
    /// </summary>
    /// <returns><see langword="true"/> if the user must allow the application in the platform's settings.</returns>
    bool RequiresApproval();

    /// <summary>
    /// Turns starting at sign-in on or off for the current user. On Windows, turning it on also removes Task Manager's
    /// override, if the user disabled the entry there.
    /// </summary>
    /// <param name="enabled">Whether the platform starts the application at sign-in.</param>
    /// <exception cref="IOException">The registry or the login item could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The registry could not be written.</exception>
    /// <exception cref="System.Security.SecurityException">The registry could not be written.</exception>
    void SetEnabled(bool enabled);
}
