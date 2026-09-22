using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Notifications;

namespace Pisum.Transcribe.Tray;

/// <summary>
/// The <see cref="INotifier"/> that shows a notification as a balloon tip of the tray icon.
/// </summary>
internal sealed class TrayBalloonNotifier : INotifier
{
    private readonly IUiDispatcher _uiDispatcher;
    private readonly TrayIconService _trayIcon;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="uiDispatcher">Reaches the UI thread, which the tray icon needs.</param>
    /// <param name="trayIcon">The tray icon that shows the balloon tip.</param>
    public TrayBalloonNotifier(IUiDispatcher uiDispatcher, TrayIconService trayIcon)
    {
        _uiDispatcher = uiDispatcher;
        _trayIcon = trayIcon;
    }

    /// <inheritdoc />
    public void Show(string title, string message)
    {
        // Queued on the UI thread even when called there. A balloon after the icon was removed fails inside the
        // discarded task.
        _ = _uiDispatcher.InvokeAsync(() => _trayIcon.ShowNotification(title, message));
    }
}
