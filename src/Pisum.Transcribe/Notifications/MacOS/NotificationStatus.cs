namespace Pisum.Transcribe.Notifications;

/// <summary>
/// The answer of the macOS notification center, with the status codes of the Swift helper.
/// </summary>
internal enum NotificationStatus
{
    /// <summary>
    /// The request was accepted, or the user allowed notifications.
    /// </summary>
    Ok = 0,

    /// <summary>
    /// The application can't show notifications: it doesn't run as an app bundle, or the helper is missing.
    /// </summary>
    Unavailable = 1,

    /// <summary>
    /// The user refused notifications.
    /// </summary>
    Denied = 2,

    /// <summary>
    /// The permission request failed.
    /// </summary>
    Failed = 3,
}
