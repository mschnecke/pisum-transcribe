namespace Pisum.Transcribe.Notifications;

/// <summary>
/// Shows notifications to the user.
/// </summary>
internal interface INotifier
{
    /// <summary>
    /// Shows a notification. It may be called from any thread and returns without waiting for the notification to
    /// show. An implementation that needs a particular thread moves there itself.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification text.</param>
    void Show(string title, string message);
}
