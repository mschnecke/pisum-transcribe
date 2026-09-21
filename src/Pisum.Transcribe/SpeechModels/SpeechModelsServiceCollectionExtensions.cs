using Microsoft.Extensions.DependencyInjection;

namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// Registers the speech models feature.
/// </summary>
internal static class SpeechModelsServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IModelStore"/>, which deletes unfinished downloads at startup, and the first-run setup, which
    /// opens the setup window when the selected model is not installed.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddSpeechModels(this IServiceCollection services)
    {
        services.AddHttpClient(ModelStore.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<ModelStore>();
        services.AddSingleton<IModelStore>(provider => provider.GetRequiredService<ModelStore>());

        // Registered before the setup, so unfinished downloads are deleted before the window can start a new one.
        services.AddHostedService(provider => provider.GetRequiredService<ModelStore>());
        services.AddHostedService<ModelSetupHostedService>();
        return services;
    }
}
