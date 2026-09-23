using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Notifications;

/// <summary>
/// The macOS notification center through the Swift helper, which calls <c>UNUserNotificationCenter</c>.
/// </summary>
internal sealed class PisumNotificationCenter : INotificationCenter
{
    private readonly MacNativeLibrary _library;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="library">The Swift helper.</param>
    public PisumNotificationCenter(MacNativeLibrary library)
    {
        _library = library;
    }

    /// <inheritdoc />
    public NotificationStatus Start()
    {
        return _library.IsAvailable ? (NotificationStatus) PisumMac.NotificationsStart() : NotificationStatus.Unavailable;
    }

    /// <inheritdoc />
    public NotificationStatus RequestAuthorization(Action<NotificationStatus> completed)
    {
        return CallWithCallback(PisumMac.NotificationsRequest, status => completed((NotificationStatus) status));
    }

    /// <inheritdoc />
    public NotificationStatus ReadAuthorization(Action<NotificationAuthorization> completed)
    {
        return CallWithCallback(PisumMac.NotificationsStatus, status => completed((NotificationAuthorization) status));
    }

    /// <inheritdoc />
    public NotificationStatus Notify(string title, string message)
    {
        return _library.IsAvailable ? (NotificationStatus) PisumMac.Notify(title, message) : NotificationStatus.Unavailable;
    }

    private NotificationStatus CallWithCallback(Func<PisumMac.StatusCallback, nint, int> function, Action<int> completed)
    {
        if (!_library.IsAvailable)
        {
            return NotificationStatus.Unavailable;
        }

        var context = PisumMac.CreateContext(completed);
        var status = (NotificationStatus) function(PisumMac.Callback, context);
        if (status == NotificationStatus.Unavailable)
        {
            PisumMac.FreeContext(context);
        }

        return status;
    }
}
