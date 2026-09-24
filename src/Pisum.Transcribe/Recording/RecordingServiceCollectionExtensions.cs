using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpHook;
#if !WINDOWS
using Pisum.Transcribe.Hosting;
using SharpHook.Providers;
#endif

namespace Pisum.Transcribe.Recording;

/// <summary>
/// Registers the recording feature.
/// </summary>
internal static class RecordingServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IPushToTalkHotkey"/>, which runs the keyboard hook while the host runs, and
    /// <see cref="IAudioRecorder"/>, which the container disposes at shutdown, releasing the microphone. On macOS the
    /// hook runs only with the Accessibility grant in effect, which <c>AddPermissions()</c> provides.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddRecording(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

#if WINDOWS
        services.AddSingleton<IHotkeyKeyState, WindowsHotkeyKeyState>();
        services.AddSingleton<IHookAccess, WindowsHookAccess>();
#else
        services.AddSingleton<IHotkeyKeyState, MacHotkeyKeyState>();
        services.AddSingleton<IHookAccess, MacHookAccess>();
#endif

        // Handlers run on the hook's own event-loop thread, so a slow handler never delays system input.
        services.AddSingleton<IGlobalHook>(_ =>
        {
#if !WINDOWS
            // Only the setup window asks for the Accessibility grant. KeyTypedEnabled stays off, because libuiohook
            // would send every key to the main queue from inside the event tap (design D9 of add-macos-recording).
            UioHookProvider.Instance.PromptUserIfAxApiDisabled = false;
#endif
            return new EventLoopGlobalHook(useBackgroundThreadForEventLoop: true);
        });
        services.AddSingleton<SharpHookPushToTalkHotkey>();
        services.AddSingleton<IPushToTalkHotkey>(provider => provider.GetRequiredService<SharpHookPushToTalkHotkey>());
        services.AddHostedService(provider => provider.GetRequiredService<SharpHookPushToTalkHotkey>());

#if WINDOWS
        services.AddSingleton<ICaptureSessionFactory, WasapiCaptureSessionFactory>();
#else
        services.TryAddSingleton<MacNativeLibrary>();
        services.AddSingleton<ICaptureSessionFactory, AudioQueueCaptureSessionFactory>();
#endif
        services.AddSingleton<IAudioRecorder, AudioRecorder>();
        return services;
    }
}
