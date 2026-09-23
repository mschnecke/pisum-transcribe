using Microsoft.Extensions.DependencyInjection;
#if !WINDOWS
using Pisum.Transcribe.Hosting;
#endif

namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// Registers the speech models feature.
/// </summary>
internal static class SpeechModelsServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IModelStore"/>, which deletes unfinished downloads at startup, and the first-run setup, which
    /// opens the setup window when the selected model is not installed. On macOS the store counts purgeable space as
    /// free and excludes the models folder from Time Machine.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddSpeechModels(this IServiceCollection services)
    {
        services.AddHttpClient(ModelStore.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
#if WINDOWS
        services.AddSingleton<ModelStore>();
#else
        services.AddSingleton<MacFreeSpace>();
        services.AddSingleton(provider => ActivatorUtilities.CreateInstance<ModelStore>(provider,
            (Func<string, long>) provider.GetRequiredService<MacFreeSpace>().GetAvailableFreeSpace,
            (Action<string>) CoreFoundation.ExcludeFromBackup));
#endif
        services.AddSingleton<IModelStore>(provider => provider.GetRequiredService<ModelStore>());

        // Registered before the setup, so unfinished downloads are deleted before the window can start a new one.
        services.AddHostedService(provider => provider.GetRequiredService<ModelStore>());
        services.AddSingleton<ModelSetupHostedService>();
        services.AddSingleton<ISetupWindow>(provider => provider.GetRequiredService<ModelSetupHostedService>());
        services.AddHostedService(provider => provider.GetRequiredService<ModelSetupHostedService>());
        return services;
    }
}
