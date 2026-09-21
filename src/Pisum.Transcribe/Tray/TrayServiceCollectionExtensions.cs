using Microsoft.Extensions.DependencyInjection;

namespace Pisum.Transcribe.Tray;

/// <summary>
/// Registers the tray feature.
/// </summary>
internal static class TrayServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="ITrayIconService"/>. Resolve it on the UI thread.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddTray(this IServiceCollection services)
    {
        services.AddSingleton<ITrayIconService, TrayIconService>();
        return services;
    }
}
