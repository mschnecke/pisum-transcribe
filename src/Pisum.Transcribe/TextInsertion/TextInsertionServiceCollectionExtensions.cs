using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
#if !WINDOWS
using Pisum.Transcribe.Hosting;
#endif

namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// Registers the text insertion feature.
/// </summary>
internal static class TextInsertionServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="ITextInserter"/> and <see cref="IForegroundWindowTracker"/> with the platform's clipboard,
    /// keyboard input and secure input check. The inserter is also a hosted service, whose stop waits for a pending
    /// clipboard restore; the container disposes the clipboard thread and the keystroke simulator only after all hosted
    /// services have stopped. On macOS the keyboard input is a hosted service too, which reads the keyboard layout on
    /// the UI thread.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddTextInsertion(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
#if WINDOWS
        services.AddSingleton<IForegroundWindowTracker, ForegroundWindowTracker>();
        services.AddSingleton<IClipboardService, Win32ClipboardService>();
        services.AddSingleton<IKeyboardInput, SharpHookKeyboardInput>();
        services.AddSingleton<ISecureInput, NoSecureInput>();

        // The process token is read once.
        var isSelfElevated = ProcessElevation.IsElevated((uint) Environment.ProcessId, out _) ?? false;
#else
        services.TryAddSingleton<MacNativeLibrary>();
        services.AddSingleton<IFocusedWindowReader, AccessibilityFocusedWindowReader>();
        services.AddSingleton<MacForegroundWindowTracker>();
        services.AddSingleton<IForegroundWindowTracker>(provider =>
            provider.GetRequiredService<MacForegroundWindowTracker>());
        services.AddSingleton<IClipboardService, MacClipboardService>();
        services.AddSingleton<IMacKeyEvents, CoreGraphicsKeyEvents>();
        services.AddSingleton<IKeyboardLayout, MacKeyboardLayout>();
        services.AddSingleton<MacKeyboardInput>();
        services.AddSingleton<IKeyboardInput>(provider => provider.GetRequiredService<MacKeyboardInput>());
        services.AddHostedService(provider => provider.GetRequiredService<MacKeyboardInput>());
        services.AddSingleton<ISecureInput, MacSecureInput>();

        // macOS has no elevated windows; secure input is checked instead.
        const bool isSelfElevated = false;
#endif
        services.AddSingleton(provider => ActivatorUtilities.CreateInstance<TextInserter>(provider, isSelfElevated));
        services.AddSingleton<ITextInserter>(provider => provider.GetRequiredService<TextInserter>());
        services.AddHostedService(provider => provider.GetRequiredService<TextInserter>());
        return services;
    }
}
