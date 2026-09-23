using Avalonia.Threading;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// The <see cref="IUiDispatcher"/> over Avalonia's UI thread dispatcher.
/// </summary>
internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public Task InvokeAsync(Action action)
    {
        // InvokeAsync, not Post: it keeps an exception in the returned task, and Post sends it to
        // Dispatcher.UnhandledException.
        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }
}
