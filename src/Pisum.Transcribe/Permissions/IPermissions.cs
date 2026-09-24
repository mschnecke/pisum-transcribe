namespace Pisum.Transcribe.Permissions;

/// <summary>
/// Reads and requests the permissions Pisum Transcribe needs on macOS. Call the members on the UI thread, except
/// <see cref="ProbePasteboard"/>.
/// </summary>
internal interface IPermissions
{
    /// <summary>
    /// Whether the Accessibility grant is in effect for the keyboard hook: the process had it when it started, and it
    /// wasn't revoked since. macOS makes a later grant visible to the hook only in a new process, so a grant made while
    /// the process runs, also after a revoke, is never in effect.
    /// </summary>
    bool IsAccessibilityInEffect { get; }

    /// <summary>
    /// Raised when <see cref="IsAccessibilityInEffect"/> changes, on the thread that changed it: the UI thread.
    /// </summary>
    event EventHandler? AccessibilityInEffectChanged;

    /// <summary>
    /// Records that the keyboard hook lost the Accessibility grant while it ran. From then on the grant isn't in effect
    /// until the process restarts.
    /// </summary>
    void OnAccessibilityRevoked();

    /// <summary>
    /// Reads the state of <see cref="Permission.Accessibility"/>, <see cref="Permission.Microphone"/> or
    /// <see cref="Permission.PasteFromOtherApps"/>. The check is cheap, so it may run on every refresh of the window
    /// and every time the menu opens.
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
