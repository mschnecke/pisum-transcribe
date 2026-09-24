using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
#if !WINDOWS
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.TextInsertion;
#endif

namespace Pisum.Transcribe.Dictation;

/// <summary>
/// Registers the dictation feature.
/// </summary>
internal static class DictationServiceCollectionExtensions
{
    /// <summary>
    /// Adds the dictation controller, which connects the hotkey, the recorder, the voice activity detector, the
    /// transcriber and the text inserter, and its feedback, which owns the tray icon and the recording overlay. Register
    /// it after those features, so the host starts it last and stops it first.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddDictation(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<DictationState>();
        services.AddSingleton<IDictationState>(provider => provider.GetRequiredService<DictationState>());
#if WINDOWS
        services.AddSingleton<IHotkeyAvailability, AlwaysAvailableHotkey>();
        services.AddSingleton<IOverlayPlatform, Win32OverlayPlatform>();
#else
        services.AddSingleton<MacHotkeyAvailability>();
        services.AddSingleton<IHotkeyAvailability>(provider => provider.GetRequiredService<MacHotkeyAvailability>());

        // Before the feedback, so the availability is known at the tray's first render.
        services.AddHostedService(provider => provider.GetRequiredService<MacHotkeyAvailability>());
        services.AddSingleton<IOverlayPlatform>(provider => new MacOverlayPlatform(
            provider.GetRequiredService<MacForegroundWindowTracker>(), provider.GetRequiredService<MacNativeLibrary>()));
#endif
        services.AddSingleton<DictationFeedback>();
        services.AddSingleton<IDictationFeedback>(provider => provider.GetRequiredService<DictationFeedback>());

        // Registered before the controller, so the feedback shows the engine status before the first press.
        services.AddHostedService(provider => provider.GetRequiredService<DictationFeedback>());
        services.AddHostedService<DictationController>();
        return services;
    }
}
