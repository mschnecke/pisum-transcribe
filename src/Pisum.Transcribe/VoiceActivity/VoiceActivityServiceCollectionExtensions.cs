using Microsoft.Extensions.DependencyInjection;

namespace Pisum.Transcribe.VoiceActivity;

/// <summary>
/// Registers the voice activity detection feature.
/// </summary>
internal static class VoiceActivityServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IVoiceActivityDetector"/> and its warm-up at startup. Register it before the dictation feature,
    /// so the host starts the dictation controller last. The warm-up needs the host's
    /// <see cref="Hosting.IProcessActivity"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddVoiceActivity(this IServiceCollection services)
    {
        services.AddSingleton<IVoiceActivityDetector, SileroVoiceActivityDetector>();
        services.AddHostedService<VoiceActivityWarmupService>();
        return services;
    }
}
