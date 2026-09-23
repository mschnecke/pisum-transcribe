using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
#if WINDOWS
using Pisum.Transcribe.SettingsWindow;
#else
using Pisum.Transcribe.Hosting;
#endif

namespace Pisum.Transcribe.Notifications;

/// <summary>
/// Registers the notifications feature.
/// </summary>
internal static class NotificationsServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="INotifier"/>: on Windows as toasts, with <c>ToastRegistration</c>, which registers the
    /// application's AppUserModelID at startup, and on macOS as notifications from the app bundle, with
    /// <c>MacNotifier</c>, which asks for permission at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
#if WINDOWS
        // Shared with the settings window's startup entry.
        services.TryAddSingleton<IUserRegistry, UserRegistry>();
        services.AddSingleton<IToastSender, WinRtToastSender>();
        services.AddSingleton<INotifier, ToastNotifier>();
        services.AddHostedService<ToastRegistration>();
#else
        // Shared with the quit event's sender.
        services.TryAddSingleton<MacNativeLibrary>();
        services.AddSingleton<INotificationCenter, PisumNotificationCenter>();
        services.AddSingleton<MacNotifier>();
        services.AddSingleton<INotifier>(provider => provider.GetRequiredService<MacNotifier>());
        services.AddHostedService(provider => provider.GetRequiredService<MacNotifier>());
#endif
        return services;
    }
}
