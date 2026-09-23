using Avalonia.Threading;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class AvaloniaUiDispatcherTests
{
    [Fact]
    public Task InvokeAsync_FromOtherThread_RunsOnDispatcherThread()
    {
        // The UI thread runs the dispatcher only while the test body awaits, so the other thread is awaited inside it.
        return HeadlessUi.RunAsync(async () =>
        {
            // Arrange
            var sut = new AvaloniaUiDispatcher();
            var onUiThread = false;

            // Act
            await Task.Run(() => sut.InvokeAsync(() => onUiThread = Dispatcher.UIThread.CheckAccess()),
                TestContext.Current.CancellationToken);

            // Assert
            onUiThread.ShouldBeTrue();
        });
    }

    [Fact]
    public Task InvokeAsync_OnDispatcherThread_RunsAfterCurrentOperation()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            // Arrange
            var sut = new AvaloniaUiDispatcher();
            var events = new List<string>();

            // Act
            var queued = sut.InvokeAsync(() => events.Add("queued action"));
            events.Add("current operation");
            await queued;

            // Assert
            events.ShouldBe(["current operation", "queued action"]);
        });
    }

    [Fact]
    public Task InvokeAsync_ActionThrows_FaultsTaskWithoutUnhandledException()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            // Arrange
            var sut = new AvaloniaUiDispatcher();
            var unhandled = false;
            DispatcherUnhandledExceptionEventHandler handler = (_, e) =>
            {
                unhandled = true;
                e.Handled = true;
            };
            Dispatcher.UIThread.UnhandledException += handler;

            try
            {
                // Act
                var task = sut.InvokeAsync(() => throw new InvalidOperationException("Test failure"));

                // Assert
                await Should.ThrowAsync<InvalidOperationException>(task);
                unhandled.ShouldBeFalse();
            }
            finally
            {
                Dispatcher.UIThread.UnhandledException -= handler;
            }
        });
    }
}
