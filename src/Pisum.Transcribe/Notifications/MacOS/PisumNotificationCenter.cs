using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Notifications;

/// <summary>
/// The macOS notification center through the Swift helper, which calls <c>UNUserNotificationCenter</c>.
/// </summary>
internal sealed class PisumNotificationCenter : INotificationCenter
{
    private readonly MacNativeLibrary _library;

    // Referenced, so the delegate isn't collected while the helper may still call it.
    private PisumMac.NotificationsStartCallback? _callback;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="library">The Swift helper.</param>
    public PisumNotificationCenter(MacNativeLibrary library)
    {
        _library = library;
    }

    /// <inheritdoc />
    public NotificationStatus Start(Action<NotificationStatus> authorizationCompleted)
    {
        if (!_library.IsAvailable)
        {
            return NotificationStatus.Unavailable;
        }

        _callback = (_, status) => authorizationCompleted((NotificationStatus) status);
        return (NotificationStatus) PisumMac.NotificationsStart(_callback, 0);
    }

    /// <inheritdoc />
    public NotificationStatus Notify(string title, string message)
    {
        return _library.IsAvailable ? (NotificationStatus) PisumMac.Notify(title, message) : NotificationStatus.Unavailable;
    }
}
