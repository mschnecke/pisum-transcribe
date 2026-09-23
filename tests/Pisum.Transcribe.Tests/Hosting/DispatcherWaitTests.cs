using Avalonia.Threading;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class DispatcherWaitTests
{
    [Fact]
    public Task Until_TaskCompletesInDispatcherOperation_ReturnsWithoutFurtherOperation()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange: nothing is queued after the operation that completes the task, so only the synchronous
            // continuation can end the frame.
            var shutdown = new TaskCompletionSource();
            Dispatcher.UIThread.Post(() => shutdown.SetResult());

            // Act
            DispatcherWait.Until(shutdown.Task);

            // Assert
            shutdown.Task.IsCompleted.ShouldBeTrue();
        });
    }

    [Fact]
    public Task Until_TaskAlreadyCompleted_ReturnsWithoutRunningQueuedOperations()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var events = new List<string>();
            Dispatcher.UIThread.Post(() => events.Add("queued operation"));

            // Act
            DispatcherWait.Until(Task.CompletedTask);
            events.Add("wait returned");
            Dispatcher.UIThread.RunJobs();

            // Assert
            events.ShouldBe(["wait returned", "queued operation"]);
        });
    }

    [Fact]
    public Task Until_TaskCompletesOnOtherThread_ReturnsAfterIt()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var work = Task.Run(() => Task.Delay(TimeSpan.FromMilliseconds(100)));

            // Act
            DispatcherWait.Until(work);

            // Assert
            work.IsCompleted.ShouldBeTrue();
            Dispatcher.UIThread.CheckAccess().ShouldBeTrue();
        });
    }
}
