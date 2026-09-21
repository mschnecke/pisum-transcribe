using Microsoft.Extensions.DependencyInjection;

namespace Pisum.Transcribe.Transcription;

/// <summary>
/// Registers the transcription feature.
/// </summary>
internal static class TranscriptionServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="ITranscriber"/>, which releases the model on shutdown, and the background loading of the selected
    /// model with its failure notification.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddTranscription(this IServiceCollection services)
    {
        services.AddSingleton<INativeSpeechEngineFactory, TranscribeCppEngineFactory>();
        services.AddSingleton<TranscribeCppTranscriber>();
        services.AddSingleton<ITranscriber>(provider => provider.GetRequiredService<TranscribeCppTranscriber>());

        // Registered before the loading service, so the host stops the loading service first and the engine last.
        services.AddHostedService(provider => provider.GetRequiredService<TranscribeCppTranscriber>());
        services.AddHostedService<TranscriberHostedService>();
        return services;
    }
}
