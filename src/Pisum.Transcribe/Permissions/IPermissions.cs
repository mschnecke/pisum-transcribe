namespace Pisum.Transcribe.Permissions;

/// <summary>
/// Reads and requests the permissions Pisum Transcribe needs on macOS. Call the members on the UI thread, except
/// <see cref="ProbePasteboard"/>.
/// </summary>
internal interface IPermissions
{
    /// <summary>
    /// Whether the process had the Accessibility grant when it started. macOS makes a later grant visible to the
    /// keyboard hook only in a new process, so only this grant is in effect.
    /// </summary>
    bool IsAccessibilityGrantedAtStart { get; }

    /// <summary>
    /// Reads the state of <see cref="Permission.Accessibility"/>, <see cref="Permission.Microphone"/> or
    /// <see cref="Permission.PasteFromOtherApps"/>. The check is cheap and may also run on another thread.
    /// </summary>
    /// <param name="permission">The permission, not <see cref="Permission.Notifications"/>.</param>
    /// <returns>The state.</returns>
    PermissionState GetState(Permission permission);

    /// <summary>
    /// Reads the state of <see cref="Permission.Notifications"/>, which macOS reports asynchronously.
    /// </summary>
    /// <returns>The state.</returns>
    Task<PermissionState> GetNotificationsStateAsync();

    /// <summary>
    /// Shows macOS's Accessibility prompt, which leads to System Settings. macOS shows it only the first time.
    /// </summary>
    void PromptForAccessibility();

    /// <summary>
    /// Asks for the microphone. macOS shows its prompt only while the permission wasn't asked yet.
    /// </summary>
    /// <returns>The new state.</returns>
    Task<PermissionState> RequestMicrophoneAsync();

    /// <summary>
    /// Asks for permission to show notifications.
    /// </summary>
    /// <returns>A task that completes when the user answered.</returns>
    Task RequestNotificationsAsync();

    /// <summary>
    /// Reads the pasteboard once, so that macOS asks the user now. <b>Never call it on the UI thread</b>: macOS's alert
    /// blocks the reading thread until the user answers.
    /// </summary>
    void ProbePasteboard();
}
