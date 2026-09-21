using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class DispatcherWaitTests
{
    [Fact]
    public void Until_TaskCompletesInDispatcherOperation_ReturnsBeforeOperationQueuedByIt()
    {
        RunOnStaThread(dispatcher =>
        {
            // Arrange
            var events = new List<string>();
            var shutdown = new TaskCompletionSource();
            dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                dispatcher.BeginInvoke(DispatcherPriority.Normal, () => events.Add("queued operation"));
                shutdown.SetResult();
            });

            // Act
            DispatcherWait.Until(shutdown.Task);
            events.Add("wait returned");
            ProcessQueuedOperations(dispatcher);

            // Assert
            events.ShouldBe(["wait returned", "queued operation"]);
        });
    }

    [Fact]
    public void Until_TaskAlreadyCompleted_ReturnsWithoutRunningQueuedOperations()
    {
        RunOnStaThread(dispatcher =>
        {
            // Arrange
            var events = new List<string>();
            dispatcher.BeginInvoke(DispatcherPriority.Normal, () => events.Add("queued operation"));

            // Act
            DispatcherWait.Until(Task.CompletedTask);
            events.Add("wait returned");
            ProcessQueuedOperations(dispatcher);

            // Assert
            events.ShouldBe(["wait returned", "queued operation"]);
        });
    }

    /// <summary>
    /// Runs the operations queued so far, by waiting for one queued behind them.
    /// </summary>
    private static void ProcessQueuedOperations(Dispatcher dispatcher)
    {
        dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Runs the test body on an STA thread with its own dispatcher, set as the synchronization context as on the UI
    /// thread, so that an await would resume through the dispatcher.
    /// </summary>
    private static void RunOnStaThread(Action<Dispatcher> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                action(dispatcher);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }
}
