namespace Pisum.Transcribe.Notifications;

/// <summary>
/// Whether the user allowed notifications, with the status codes of the Swift helper.
/// </summary>
internal enum NotificationAuthorization
{
    /// <summary>
    /// The user wasn't asked yet.
    /// </summary>
    NotDetermined = 0,

    /// <summary>
    /// The user refused notifications.
    /// </summary>
    Denied = 2,

    /// <summary>
    /// The user allowed notifications, also provisionally.
    /// </summary>
    Authorized = 3,
}
