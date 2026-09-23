using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Pisum.Transcribe.Tray;

/// <summary>
/// Registers the tray feature.
/// </summary>
internal static class TrayServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="ITrayIconService"/> and <see cref="ITaskbarModeWatcher"/>, which the icon follows. Resolve
    /// <see cref="ITrayIconService"/> on the UI thread.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddTray(this IServiceCollection services)
    {
        // The container disposes the watcher, which stops its thread.
        services.AddSingleton(provider =>
            new TaskbarModeWatcher(new PersonalizeKey(), provider.GetRequiredService<ILogger<TaskbarModeWatcher>>()));
        services.AddSingleton<ITaskbarModeWatcher>(provider => provider.GetRequiredService<TaskbarModeWatcher>());

        // One tray icon for both. A second registration would create a second icon that is never shown.
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<ITrayIconService>(provider => provider.GetRequiredService<TrayIconService>());
        return services;
    }
}
