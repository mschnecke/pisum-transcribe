using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.SettingsWindow;

namespace Pisum.Transcribe.Notifications;

/// <summary>
/// Registers the application's AppUserModelID for the current user at every start, so that Windows shows its toasts
/// with the name and the icon of Pisum Transcribe. Writing it each time also repairs a deleted key, and works for a
/// build that isn't installed. Uninstalling removes the key.
/// </summary>
internal sealed class ToastRegistration : IHostedService
{
    /// <summary>
    /// The AppUserModelID of the application's toasts. Windows keys the user's notification settings to it, so it never
    /// changes.
    /// </summary>
    public const string AppUserModelId = "Pisum.Transcribe";

    /// <summary>
    /// The registry key of the AppUserModelID, below <c>HKEY_CURRENT_USER</c>.
    /// </summary>
    public const string KeyPath = @"Software\Classes\AppUserModelId\" + AppUserModelId;

    /// <summary>
    /// The sender name Windows shows on a toast.
    /// </summary>
    public const string DisplayName = "Pisum Transcribe";

    private readonly IUserRegistry _registry;
    private readonly ILogger<ToastRegistration> _logger;
    private readonly string _iconPath;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="registry">The current user's registry.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="baseDirectory">
    /// The folder of the executable, which holds <c>TrayIcon.png</c>, for tests. <see langword="null"/> uses
    /// <see cref="AppContext.BaseDirectory"/>.
    /// </param>
    public ToastRegistration(IUserRegistry registry, ILogger<ToastRegistration> logger, string? baseDirectory = null)
    {
        _registry = registry;
        _logger = logger;
        _iconPath = Path.GetFullPath(Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "TrayIcon.png"));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Without the registration toasts don't show, but the application still works.
        try
        {
            _registry.SetValue(KeyPath, "DisplayName", DisplayName);
            _registry.SetValue(KeyPath, "IconUri", _iconPath);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not register the application for notifications");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
