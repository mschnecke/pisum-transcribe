using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Notifications;

/// <summary>
/// The <see cref="INotifier"/> that shows a notification from Pisum Transcribe in the macOS notification center. It
/// asks for permission when the host starts, which macOS shows the user once. The notification has no actions, and it
/// stays in the notification center after the application has ended.
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
            _unavailable = _center.Start(OnAuthorizationCompleted) == NotificationStatus.Unavailable;
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

    private void OnAuthorizationCompleted(NotificationStatus status)
    {
        _denied = status != NotificationStatus.Ok;
        if (status == NotificationStatus.Failed)
        {
            _logger.LogWarning("Could not ask for permission to show notifications");
        }
        else
        {
            _logger.LogInformation("Notifications are {State}", status == NotificationStatus.Ok ? "allowed" : "not allowed");
        }
    }
}
