using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pisum.Transcribe.SettingsWindow;

namespace Pisum.Transcribe.Notifications;

/// <summary>
/// Registers the notifications feature.
/// </summary>
internal static class NotificationsServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="INotifier"/> as Windows toasts, and <see cref="ToastRegistration"/>, which registers the
    /// application's AppUserModelID at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        // Shared with the settings window's startup entry.
        services.TryAddSingleton<IUserRegistry, UserRegistry>();
        services.AddSingleton<IToastSender, WinRtToastSender>();
        services.AddSingleton<INotifier, ToastNotifier>();
        services.AddHostedService<ToastRegistration>();
        return services;
    }
}
