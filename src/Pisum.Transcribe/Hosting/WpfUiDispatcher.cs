using System.Windows.Threading;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// The <see cref="IUiDispatcher"/> over WPF's dispatcher.
/// </summary>
internal sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="dispatcher">The dispatcher of the UI thread.</param>
    public WpfUiDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    public Task InvokeAsync(Action action)
    {
        // InvokeAsync, not BeginInvoke: it keeps an exception in the returned task, and BeginInvoke sends it to
        // Dispatcher.UnhandledException.
        return _dispatcher.InvokeAsync(action).Task;
    }
}
