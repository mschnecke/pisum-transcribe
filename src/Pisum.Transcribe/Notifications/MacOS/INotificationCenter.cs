namespace Pisum.Transcribe.Notifications;

/// <summary>
/// The macOS notification center. Call the members on the UI thread.
/// </summary>
internal interface INotificationCenter
{
    /// <summary>
    /// Asks for permission to show notifications. macOS asks the user once and remembers the answer.
    /// </summary>
    /// <param name="authorizationCompleted">
    /// Receives the answer later on a background thread, unless the result is <see cref="NotificationStatus.Unavailable"/>:
    /// <see cref="NotificationStatus.Ok"/>, <see cref="NotificationStatus.Denied"/> or
    /// <see cref="NotificationStatus.Failed"/>.
    /// </param>
    /// <returns><see cref="NotificationStatus.Ok"/> when asked, or <see cref="NotificationStatus.Unavailable"/>.</returns>
    NotificationStatus Start(Action<NotificationStatus> authorizationCompleted);

    /// <summary>
    /// Adds a notification.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification text.</param>
    /// <returns><see cref="NotificationStatus.Ok"/> when added, or <see cref="NotificationStatus.Unavailable"/>.</returns>
    NotificationStatus Notify(string title, string message);
}
