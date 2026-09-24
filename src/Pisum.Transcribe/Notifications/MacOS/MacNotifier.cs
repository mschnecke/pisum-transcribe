using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Notifications;

/// <summary>
/// The <see cref="INotifier"/> that shows a notification from Pisum Transcribe in the macOS notification center. It
/// doesn't ask for permission, which the setup window does (design D7 of add-macos-setup), and reads at startup whether
/// the user refused. The notification has no actions, and it stays in the notification center after the application
/// has ended.
/// </summary>
/// <remarks>
/// Outside an app bundle macOS can't show notifications, so each one is written to the log instead. After the user
/// refused them, each one is written to the log at Debug and dropped.
/// </remarks>
internal sealed class MacNotifier : INotifier, IHostedService
{
    private readonly INotificationCenter _center;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger<MacNotifier> _logger;

    // Only touched on the UI thread.
    private bool _unavailable;

    // Set on the helper's thread.
    private volatile bool _denied;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="center">The notification center.</param>
    /// <param name="uiDispatcher">Reaches the UI thread, where the notification center is called.</param>
    /// <param name="logger">The logger.</param>
    public MacNotifier(INotificationCenter center, IUiDispatcher uiDispatcher, ILogger<MacNotifier> logger)
    {
        _center = center;
        _uiDispatcher = uiDispatcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Show(string title, string message)
    {
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            if (_denied)
            {
                _logger.LogDebug("Notifications are not allowed, dropped the notification {Title}", title);
                return;
            }

            if (!_unavailable && _center.Notify(title, message) == NotificationStatus.Ok)
            {
                return;
            }

            _unavailable = true;
            _logger.LogInformation("Notification: {Title}: {Message}", title, message);
        });
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() =>
        {
            _unavailable = _center.Start() == NotificationStatus.Unavailable
                           || _center.ReadAuthorization(OnAuthorizationRead) == NotificationStatus.Unavailable;
            if (_unavailable)
            {
                _logger.LogInformation(
                    "Notifications are written to the log, because the application doesn't run as an app bundle");
            }
        });
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void OnAuthorizationRead(NotificationAuthorization authorization)
    {
        _denied = authorization == NotificationAuthorization.Denied;
        _logger.LogInformation("Notifications are {State}", authorization switch
        {
            NotificationAuthorization.Authorized => "allowed",
            NotificationAuthorization.Denied => "not allowed",
            _ => "not asked yet",
        });
    }
}
