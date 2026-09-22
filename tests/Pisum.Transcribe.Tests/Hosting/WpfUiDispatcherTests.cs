using System.Windows.Threading;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class WpfUiDispatcherTests
{
    [Fact]
    public async Task InvokeAsync_FromOtherThread_RunsOnDispatcherThread()
    {
        // Arrange
        using var uiThread = DispatcherThread.Start();
        var sut = new WpfUiDispatcher(uiThread.Dispatcher);
        var threadId = 0;

        // Act
        await sut.InvokeAsync(() => threadId = Environment.CurrentManagedThreadId);

        // Assert
        threadId.ShouldBe(uiThread.ManagedThreadId);
    }

    [Fact]
    public async Task InvokeAsync_OnDispatcherThread_RunsAfterCurrentOperation()
    {
        // Arrange
        using var uiThread = DispatcherThread.Start();
        var sut = new WpfUiDispatcher(uiThread.Dispatcher);
        var events = new List<string>();
        Task? queued = null;

        // Act
        uiThread.Dispatcher.Invoke(() =>
        {
            queued = sut.InvokeAsync(() => events.Add("queued action"));
            events.Add("current operation");
        });
        await queued!;

        // Assert
        events.ShouldBe(["current operation", "queued action"]);
    }

    [Fact]
    public async Task InvokeAsync_ActionThrows_FaultsTaskWithoutUnhandledException()
    {
        // Arrange
        using var uiThread = DispatcherThread.Start();
        var sut = new WpfUiDispatcher(uiThread.Dispatcher);
        var unhandled = false;
        uiThread.Dispatcher.Invoke(() => uiThread.Dispatcher.UnhandledException += (_, e) =>
        {
            unhandled = true;
            e.Handled = true;
        });

        // Act
        var task = sut.InvokeAsync(() => throw new InvalidOperationException("Test failure"));

        // Assert
        await Should.ThrowAsync<InvalidOperationException>(task);
        uiThread.Dispatcher.Invoke(() => unhandled).ShouldBeFalse();
    }

    /// <summary>
    /// An STA thread that runs a WPF dispatcher, as the UI thread does, until it is disposed.
    /// </summary>
    private sealed class DispatcherThread : IDisposable
    {
        private readonly Thread _thread;

        private DispatcherThread(Thread thread, Dispatcher dispatcher)
        {
            _thread = thread;
            Dispatcher = dispatcher;
        }

        public Dispatcher Dispatcher { get; }

        public int ManagedThreadId => _thread.ManagedThreadId;

        public static DispatcherThread Start()
        {
            var started = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                started.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }) {IsBackground = true, Name = "Test dispatcher"};
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            var dispatcher = started.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            return new DispatcherThread(thread, dispatcher);
        }

        public void Dispose()
        {
            Dispatcher.InvokeShutdown();
            _thread.Join();
        }
    }
}
