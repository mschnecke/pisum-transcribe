using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
#if !WINDOWS
using Pisum.Transcribe.Hosting;
#endif

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// Registers the settings window feature.
/// </summary>
internal static class SettingsWindowServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IStartupRegistration"/>, the startup entry on Windows, which is also updated at startup, and the
    /// login item on macOS, the <see cref="SettingsApplier"/>, which applies saved settings while the application runs,
    /// and the <see cref="SettingsWindowService"/>, which opens the settings window from the tray. Register it after
    /// the features whose settings it changes.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddSettingsWindow(this IServiceCollection services)
    {
#if WINDOWS
        // Shared with the notification registration.
        services.TryAddSingleton<IUserRegistry, UserRegistry>();
        services.AddSingleton<StartupRegistration>();
        services.AddSingleton<IStartupRegistration>(provider => provider.GetRequiredService<StartupRegistration>());
        services.AddHostedService(provider => provider.GetRequiredService<StartupRegistration>());
#else
        services.TryAddSingleton<MacNativeLibrary>();
        services.AddSingleton<IStartupRegistration, MacLoginItem>();
#endif
        services.AddHostedService<SettingsApplier>();
        services.AddHostedService<SettingsWindowService>();
        return services;
    }
}
