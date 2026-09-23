using Microsoft.Extensions.DependencyInjection;
#if WINDOWS
using Microsoft.Extensions.Logging;
#endif

namespace Pisum.Transcribe.Tray;

/// <summary>
/// Registers the tray feature.
/// </summary>
internal static class TrayServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="ITrayIconService"/> and the platform's <see cref="ITrayIconSet"/>. On Windows the icon at rest
    /// follows the taskbar's mode through <c>ITaskbarModeWatcher</c>. Resolve <see cref="ITrayIconService"/> on
    /// the UI thread.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddTray(this IServiceCollection services)
    {
#if WINDOWS
        // The container disposes the watcher, which stops its thread.
        services.AddSingleton(provider =>
            new TaskbarModeWatcher(new PersonalizeKey(), provider.GetRequiredService<ILogger<TaskbarModeWatcher>>()));
        services.AddSingleton<ITaskbarModeWatcher>(provider => provider.GetRequiredService<TaskbarModeWatcher>());
        services.AddSingleton<ITrayIconSet, WindowsTrayIconSet>();
#else
        services.AddSingleton<ITrayIconSet, MacTrayIconSet>();
#endif

        // One tray icon for both. A second registration would create a second icon that is never shown.
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<ITrayIconService>(provider => provider.GetRequiredService<TrayIconService>());
        return services;
    }
}
