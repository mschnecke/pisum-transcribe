using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests;

/// <summary>
/// An <see cref="IUiDispatcher"/> that runs the action at once, on the calling thread, and lets an exception propagate.
/// </summary>
/// <remarks>
/// It breaks the contract of <see cref="IUiDispatcher.InvokeAsync"/> on purpose, which queues the action and keeps an
/// exception in the returned task: the services discard that task, so a failing assertion inside the action would go
/// unnoticed. The queueing and the exception rule are tested once, on the real dispatcher, in
/// <c>WpfUiDispatcherTests</c>.
/// </remarks>
internal sealed class InlineUiDispatcher : IUiDispatcher
{
    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
