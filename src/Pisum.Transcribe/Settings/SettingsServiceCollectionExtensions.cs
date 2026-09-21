using Microsoft.Extensions.DependencyInjection;

namespace Pisum.Transcribe.Settings;

/// <summary>
/// Registers the settings feature.
/// </summary>
internal static class SettingsServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="ISettingsStore"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddSettings(this IServiceCollection services)
    {
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        return services;
    }
}
