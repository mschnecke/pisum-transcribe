namespace Pisum.Transcribe.Notifications;

/// <summary>
/// The macOS notification center. Call the members on the UI thread.
/// </summary>
internal interface INotificationCenter
{
    /// <summary>
    /// Prepares the center to show notifications, without asking for permission.
    /// </summary>
    /// <returns><see cref="NotificationStatus.Ok"/>, or <see cref="NotificationStatus.Unavailable"/>.</returns>
    NotificationStatus Start();

    /// <summary>
    /// Asks for permission to show notifications. macOS asks the user once and remembers the answer.
    /// </summary>
    /// <param name="completed">
    /// Receives the answer later on a background thread, unless the result is <see cref="NotificationStatus.Unavailable"/>:
    /// <see cref="NotificationStatus.Ok"/>, <see cref="NotificationStatus.Denied"/> or
    /// <see cref="NotificationStatus.Failed"/>.
    /// </param>
    /// <returns><see cref="NotificationStatus.Ok"/> when asked, or <see cref="NotificationStatus.Unavailable"/>.</returns>
    NotificationStatus RequestAuthorization(Action<NotificationStatus> completed);

    /// <summary>
    /// Reads whether the user allowed notifications.
    /// </summary>
    /// <param name="completed">
    /// Receives the answer later on a background thread, unless the result is <see cref="NotificationStatus.Unavailable"/>.
    /// </param>
    /// <returns><see cref="NotificationStatus.Ok"/> when read, or <see cref="NotificationStatus.Unavailable"/>.</returns>
    NotificationStatus ReadAuthorization(Action<NotificationAuthorization> completed);

    /// <summary>
    /// Adds a notification.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification text.</param>
    /// <returns><see cref="NotificationStatus.Ok"/> when added, or <see cref="NotificationStatus.Unavailable"/>.</returns>
    NotificationStatus Notify(string title, string message);
}
