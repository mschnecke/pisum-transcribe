using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// Registers the text insertion feature.
/// </summary>
internal static class TextInsertionServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="ITextInserter"/> and <see cref="IForegroundWindowTracker"/>. The inserter is also a hosted
    /// service, whose stop waits for a pending clipboard restore; the container disposes the clipboard thread and the
    /// keystroke simulator only after all hosted services have stopped.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddTextInsertion(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IForegroundWindowTracker, ForegroundWindowTracker>();
        services.AddSingleton<IClipboardService, WpfClipboardService>();
        services.AddSingleton<IKeyboardInput, SharpHookKeyboardInput>();
        services.AddSingleton<TextInserter>();
        services.AddSingleton<ITextInserter>(provider => provider.GetRequiredService<TextInserter>());
        services.AddHostedService(provider => provider.GetRequiredService<TextInserter>());
        return services;
    }
}
