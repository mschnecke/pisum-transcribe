using Pisum.Transcribe.Recording;

namespace Pisum.Transcribe.Tests.Recording;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class AudioQueueCaptureSessionTests
{
    private const uint BuiltIn = 42;
    private const uint AirPods = 77;

    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);

    private readonly IAudioInput _input = A.Fake<IAudioInput>();
    private readonly IDisposable _watch = A.Fake<IDisposable>();
    private readonly List<(uint Device, IAudioInputQueue Queue, SamplesAvailableHandler Handler)> _queues = [];
    private readonly Lock _lock = new();
    private Action? _defaultDeviceChanged;
    private uint _defaultDevice = BuiltIn;

    public AudioQueueCaptureSessionTests()
    {
        A.CallTo(() => _input.GetDefaultDevice()).ReturnsLazily(() => _defaultDevice);
        A.CallTo(() => _input.WatchDefaultDevice(A<Action>._)).ReturnsLazily((Action changed) =>
        {
            _defaultDeviceChanged = changed;
            return _watch;
        });
        A.CallTo(() => _input.OpenQueue(A<uint>._, A<SamplesAvailableHandler>._)).ReturnsLazily(
            (uint device, SamplesAvailableHandler handler) =>
            {
                var queue = A.Fake<IAudioInputQueue>();
                lock (_lock)
                {
                    _queues.Add((device, queue, handler));
                }

                return queue;
            });
    }

    [Fact]
    public void Start_Opened_WatchesTheDefaultDeviceAndStartsTheQueue()
    {
        // Arrange
        var sut = new AudioQueueCaptureSession(_input, BuiltIn);

        // Act
        sut.Start();

        // Assert
        _queues.ShouldHaveSingleItem().Device.ShouldBe(BuiltIn);
        A.CallTo(() => _input.WatchDefaultDevice(A<Action>._)).MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _queues[0].Queue.Start()).MustHaveHappenedOnceExactly());
    }

    [Fact]
    public void SamplesAvailable_QueueDelivers_PassesSamplesAsNotSilent()
    {
        // Arrange
        var sut = new AudioQueueCaptureSession(_input, BuiltIn);
        var received = new List<(float[] Samples, bool Silent)>();
        sut.SamplesAvailable += (samples, silent) => received.Add((samples.ToArray(), silent));
        sut.Start();

        // Act
        _queues[0].Handler([0.25f, -0.5f], false);

        // Assert
        received.ShouldHaveSingleItem().Samples.ShouldBe([0.25f, -0.5f]);
        received[0].Silent.ShouldBeFalse();
    }

    [Fact]
    public async Task DefaultDeviceChanged_NewDevice_MovesToANewQueueWithoutStopping()
    {
        // Arrange
        var sut = new AudioQueueCaptureSession(_input, BuiltIn);
        var stopped = 0;
        sut.Stopped += (_, _) => stopped++;
        sut.Start();
        var oldQueue = _queues[0].Queue;

        // Act
        _defaultDevice = AirPods;
        _defaultDeviceChanged!();
        var newQueue = await WaitForQueueAsync(2);

        // Assert
        newQueue.Device.ShouldBe(AirPods);
        A.CallTo(() => oldQueue.Dispose()).MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => newQueue.Queue.Start()).MustHaveHappenedOnceExactly());
        stopped.ShouldBe(0);
    }

    [Fact]
    public async Task DefaultDeviceChanged_SameDevice_KeepsTheQueue()
    {
        // Arrange
        var sut = new AudioQueueCaptureSession(_input, BuiltIn);
        sut.Start();

        // Act
        _defaultDeviceChanged!();
        await WaitForDeviceReadAsync();

        // Assert
        _queues.ShouldHaveSingleItem();
        A.CallTo(() => _queues[0].Queue.Dispose()).MustNotHaveHappened();
    }

    [Fact]
    public async Task DefaultDeviceChanged_NoDeviceLeft_StopsWithDisconnectedAndReleases()
    {
        // Arrange
        var sut = new AudioQueueCaptureSession(_input, BuiltIn);
        var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.Stopped += (_, exception) => stopped.TrySetResult(exception);
        sut.Start();

        // Act
        _defaultDevice = CoreAudio.UnknownObject;
        _defaultDeviceChanged!();
        var error = await stopped.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        error.ShouldBeOfType<MicrophoneDisconnectedException>();
        A.CallTo(() => _watch.Dispose()).MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _queues[0].Queue.Dispose()).MustHaveHappenedOnceExactly());
        _queues.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task DefaultDeviceChanged_NewQueueFailsToOpen_StopsWithDisconnected()
    {
        // Arrange
        var sut = new AudioQueueCaptureSession(_input, BuiltIn);
        var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.Stopped += (_, exception) => stopped.TrySetResult(exception);
        sut.Start();
        var failure = new IOException("OSStatus -50");
        A.CallTo(() => _input.OpenQueue(AirPods, A<SamplesAvailableHandler>._)).Throws(failure);

        // Act
        _defaultDevice = AirPods;
        _defaultDeviceChanged!();
        var error = await stopped.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        error.ShouldBeOfType<MicrophoneDisconnectedException>().InnerException.ShouldBeSameAs(failure);
        A.CallTo(() => _watch.Dispose()).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task StopAsync_Running_RemovesTheListenerBeforeTheQueueAndRaisesStoppedOnce()
    {
        // Arrange
        var sut = new AudioQueueCaptureSession(_input, BuiltIn);
        var stopped = new List<Exception?>();
        sut.Stopped += (_, exception) => stopped.Add(exception);
        sut.Start();

        // Act
        await sut.StopAsync();
        await sut.StopAsync();
        await sut.DisposeAsync();

        // Assert
        stopped.ShouldBe([null]);
        A.CallTo(() => _watch.Dispose()).MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _queues[0].Queue.Dispose()).MustHaveHappenedOnceExactly());
    }

    [Fact]
    public async Task DefaultDeviceChanged_AfterStopAsync_OpensNoQueue()
    {
        // Arrange
        var sut = new AudioQueueCaptureSession(_input, BuiltIn);
        sut.Start();
        var changed = _defaultDeviceChanged!;
        await sut.StopAsync();

        // Act: a notification that CoreAudio sent before the listener was removed.
        _defaultDevice = AirPods;
        changed();
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        // Assert
        _queues.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task StopAsync_NotStarted_ReturnsWithoutStopped()
    {
        // Arrange
        var sut = new AudioQueueCaptureSession(_input, BuiltIn);
        var stopped = 0;
        sut.Stopped += (_, _) => stopped++;

        // Act
        await sut.StopAsync();
        await sut.DisposeAsync();

        // Assert
        stopped.ShouldBe(0);
        A.CallTo(() => _queues[0].Queue.Dispose()).MustHaveHappenedOnceExactly();
    }

    private async Task<(uint Device, IAudioInputQueue Queue, SamplesAvailableHandler Handler)> WaitForQueueAsync(
        int count)
    {
        var deadline = DateTime.UtcNow + SignalTimeout;
        while (DateTime.UtcNow < deadline)
        {
            lock (_lock)
            {
                if (_queues.Count >= count)
                {
                    var queue = _queues[count - 1];

                    // Opened, but maybe not yet started.
                    if (Fake.GetCalls(queue.Queue).Any(call => call.Method.Name == nameof(IAudioInputQueue.Start)))
                    {
                        return queue;
                    }
                }
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"Queue {count} was not started.");
    }

    private async Task WaitForDeviceReadAsync()
    {
        var deadline = DateTime.UtcNow + SignalTimeout;
        while (DateTime.UtcNow < deadline &&
               !Fake.GetCalls(_input).Any(call => call.Method.Name == nameof(IAudioInput.GetDefaultDevice)))
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        // The move ends right after the read.
        await Task.Delay(50, TestContext.Current.CancellationToken);
    }
}
