using Microsoft.Extensions.DependencyInjection;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// Registers the settings window feature.
/// </summary>
internal static class SettingsWindowServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IStartupRegistration"/>, which also updates an existing startup entry at startup, the
    /// <see cref="SettingsApplier"/>, which applies saved settings while the application runs, and the
    /// <see cref="SettingsWindowService"/>, which opens the settings window from the tray. Register it after the
    /// features whose settings it changes.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddSettingsWindow(this IServiceCollection services)
    {
        services.AddSingleton<IUserRegistry, UserRegistry>();
        services.AddSingleton<StartupRegistration>();
        services.AddSingleton<IStartupRegistration>(provider => provider.GetRequiredService<StartupRegistration>());
        services.AddHostedService(provider => provider.GetRequiredService<StartupRegistration>());
        services.AddHostedService<SettingsApplier>();
        services.AddHostedService<SettingsWindowService>();
        return services;
    }
}
