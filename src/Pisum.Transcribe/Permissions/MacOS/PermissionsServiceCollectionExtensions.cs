using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.SpeechModels;

namespace Pisum.Transcribe.Permissions;

/// <summary>
/// Registers the permissions feature, on macOS only.
/// </summary>
internal static class PermissionsServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IPermissions"/>, the setup window's <see cref="PermissionsViewModel"/>, and
    /// <see cref="RelaunchService"/>, which restarts the application after the Accessibility grant. Outside an app
    /// bundle the permissions belong to the terminal, so the view model resolves to <see langword="null"/>, the setup
    /// window and its menu item stay as on Windows, and there is no restart (design D8 of add-macos-setup).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddPermissions(this IServiceCollection services)
    {
        services.TryAddSingleton<MacNativeLibrary>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPermissions, MacPermissions>();

        // A null view model is passed on to the constructors that take it, as the platforms without permissions do.
        services.AddSingleton(provider => CreateViewModel(provider, () =>
        {
            var library = provider.GetRequiredService<MacNativeLibrary>();
            return library.IsAvailable ? PisumMac.HasBundle() : 0;
        })!);

        services.AddSingleton(provider => new RelaunchService(
            provider.GetRequiredService<IPermissions>(),
            provider.GetService<PermissionsViewModel>(),
            provider.GetRequiredService<IModelStore>(),
            provider.GetRequiredService<ISetupWindow>(),
            provider.GetRequiredService<IUiDispatcher>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<RelaunchService>>(),
            AppBundle.FindPath(AppContext.BaseDirectory)));
        services.AddHostedService(provider => provider.GetRequiredService<RelaunchService>());
        return services;
    }

    /// <summary>
    /// Creates the view model when the process runs as an app bundle, and otherwise logs that the permissions are
    /// skipped.
    /// </summary>
    /// <param name="provider">The service provider.</param>
    /// <param name="hasBundle">Returns 1 when the process runs as an app bundle, as <c>pisum_has_bundle</c> does.</param>
    /// <returns>The view model, or <see langword="null"/> outside an app bundle.</returns>
    internal static PermissionsViewModel? CreateViewModel(IServiceProvider provider, Func<int> hasBundle)
    {
        if (hasBundle() != 1)
        {
            provider.GetRequiredService<ILogger<PermissionsViewModel>>().LogInformation(
                "Permissions are skipped, because the application doesn't run as an app bundle");
            return null;
        }

        return new PermissionsViewModel(
            provider.GetRequiredService<IPermissions>(),
            provider.GetRequiredService<IUiDispatcher>(),
            provider.GetRequiredService<TimeProvider>(),
            url => Process.Start(new ProcessStartInfo(url) {UseShellExecute = true})?.Dispose());
    }
}
