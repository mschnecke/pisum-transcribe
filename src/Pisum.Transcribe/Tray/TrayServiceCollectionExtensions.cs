using Microsoft.Extensions.DependencyInjection;
using Pisum.Transcribe.Notifications;

namespace Pisum.Transcribe.Tray;

/// <summary>
/// Registers the tray feature.
/// </summary>
internal static class TrayServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="ITrayIconService"/>, and <see cref="INotifier"/> as balloon tips of the same tray icon. Resolve
    /// <see cref="ITrayIconService"/> on the UI thread before anything resolves <see cref="INotifier"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddTray(this IServiceCollection services)
    {
        // One tray icon for both. A second registration would create a second icon that is never shown.
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<ITrayIconService>(provider => provider.GetRequiredService<TrayIconService>());
        services.AddSingleton<INotifier, TrayBalloonNotifier>();
        return services;
    }
}
