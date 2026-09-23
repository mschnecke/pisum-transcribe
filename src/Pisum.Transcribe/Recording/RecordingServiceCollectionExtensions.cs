using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
#if WINDOWS
using SharpHook;
#endif

namespace Pisum.Transcribe.Recording;

/// <summary>
/// Registers the recording feature.
/// </summary>
internal static class RecordingServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IPushToTalkHotkey"/>, which runs the keyboard hook while the host runs, and
    /// <see cref="IAudioRecorder"/>, which the container disposes at shutdown, releasing the microphone. On macOS it
    /// adds only an inactive hotkey, for the settings window, until the hook and the recording come to macOS.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddRecording(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

#if WINDOWS
        // Handlers run on the hook's own event-loop thread, so a slow handler never delays system input.
        services.AddSingleton<IGlobalHook>(_ => new EventLoopGlobalHook(useBackgroundThreadForEventLoop: true));
        services.AddSingleton<SharpHookPushToTalkHotkey>();
        services.AddSingleton<IPushToTalkHotkey>(provider => provider.GetRequiredService<SharpHookPushToTalkHotkey>());
        services.AddHostedService(provider => provider.GetRequiredService<SharpHookPushToTalkHotkey>());

        services.AddSingleton<ICaptureSessionFactory, WasapiCaptureSessionFactory>();
        services.AddSingleton<IAudioRecorder, AudioRecorder>();
#else
        services.AddSingleton<IPushToTalkHotkey, InactivePushToTalkHotkey>();
#endif
        return services;
    }
}
