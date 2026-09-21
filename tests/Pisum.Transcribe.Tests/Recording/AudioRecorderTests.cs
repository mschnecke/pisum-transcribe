using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Pisum.Transcribe.Recording;

namespace Pisum.Transcribe.Tests.Recording;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class AudioRecorderTests
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(400);
    private static readonly float[] Speech = [0.1f, -0.2f, 0.3f];
    private static readonly float[] Silence = [0f, 0f];

    private readonly FakeCaptureSessionFactory _factory = new();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly CapturingLogger<AudioRecorder> _logger = new();
    private readonly AudioRecorder _sut;
    private readonly TaskCompletionSource _maxDurationReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<(RecordingFailedException Error, bool OnThreadPool)> _failed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _maxDurationReachedCount;
    private int _failedCount;

    public AudioRecorderTests()
    {
        _sut = new AudioRecorder(_factory, _timeProvider, _logger);
        _sut.MaxDurationReached += (_, _) =>
        {
            Interlocked.Increment(ref _maxDurationReachedCount);
            _maxDurationReached.TrySetResult();
        };
        _sut.Failed += (_, error) =>
        {
            Interlocked.Increment(ref _failedCount);
            _failed.TrySetResult((error, Thread.CurrentThread.IsThreadPoolThread));
        };
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartAsync_Idle_CompletesOnFirstPacketNotMarkedSilent()
    {
        // Arrange
        var start = _sut.StartAsync(MaxDuration, Ct);
        var session = _factory.LastSession;

        // Act
        session.Deliver(Silence, silent: true);
        var completedOnSilence = start.IsCompleted;
        session.Deliver(Speech);
        await start.WaitAsync(SignalTimeout, Ct);

        // Assert
        completedOnSilence.ShouldBeFalse();
        session.StartCalls.ShouldBe(1);
        _sut.IsRecording.ShouldBeTrue();
    }

    [Fact]
    public async Task StartAsync_LeadingZerosNotMarkedSilent_CompletesOnFirstSound()
    {
        // Arrange
        var start = _sut.StartAsync(MaxDuration, Ct);
        var session = _factory.LastSession;

        // Act
        session.Deliver(Silence);
        session.Deliver([0f, -0f, 0f]);
        var completedOnZeros = start.IsCompleted;
        session.Deliver(Speech);
        await start.WaitAsync(SignalTimeout, Ct);

        // Assert
        completedOnZeros.ShouldBeFalse();
        _sut.IsRecording.ShouldBeTrue();
    }

    [Fact]
    public async Task StopAsync_AfterLeadingAndLaterSilence_ReturnsAudioWithoutLeadingSilence()
    {
        // Arrange
        var start = _sut.StartAsync(MaxDuration, Ct);
        var session = _factory.LastSession;
        session.Deliver(Silence, silent: true);
        session.Deliver(Silence);
        session.Deliver(Silence, silent: true);
        session.Deliver(Speech);
        await start.WaitAsync(SignalTimeout, Ct);
        session.Deliver(Silence, silent: true);
        session.Deliver(Silence);
        session.Deliver(Speech);

        // Act
        var clip = await _sut.StopAsync();

        // Assert
        clip.Samples.ShouldBe([..Speech, ..Silence, ..Silence, ..Speech]);
    }

    [Fact]
    public async Task StartAsync_Recording_ThrowsInvalidOperation()
    {
        // Arrange
        await StartRecordingAsync();

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(() => _sut.StartAsync(MaxDuration, Ct));
        _factory.Sessions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task StartAsync_LimitReached_ThrowsInvalidOperation()
    {
        // Arrange
        await ReachLimitAsync();

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(() => _sut.StartAsync(MaxDuration, Ct));
    }

    [Fact]
    public async Task StartAsync_Failed_ClearsErrorAndStartsNewRecording()
    {
        // Arrange
        await LoseMicrophoneAsync();

        // Act
        await StartRecordingAsync();
        var clip = await _sut.StopAsync();

        // Assert
        clip.Samples.ShouldBe(Speech);
        _factory.Sessions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task StartAsync_NoAudioWithinTimeout_ThrowsNotRespondingAndReleasesSession()
    {
        // Arrange
        var start = _sut.StartAsync(MaxDuration, Ct);
        var session = _factory.LastSession;
        session.Deliver(Silence, silent: true);

        // Act
        _timeProvider.Advance(AudioRecorder.StartTimeout - TimeSpan.FromTicks(1));
        var completedBeforeTimeout = start.IsCompleted;
        _timeProvider.Advance(TimeSpan.FromTicks(1));

        // Assert
        completedBeforeTimeout.ShouldBeFalse();
        await Should.ThrowAsync<MicrophoneNotRespondingException>(() => start.WaitAsync(SignalTimeout, Ct));
        session.IsDisposed.ShouldBeTrue();
        _sut.IsRecording.ShouldBeFalse();
        await StartRecordingAsync();
    }

    [Fact]
    public async Task StartAsync_Cancelled_ThrowsOperationCanceledAndReleasesSession()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var start = _sut.StartAsync(MaxDuration, cancellation.Token);
        var session = _factory.LastSession;

        // Act
        await cancellation.CancelAsync();

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(() => start.WaitAsync(SignalTimeout, Ct));
        session.IsDisposed.ShouldBeTrue();
        await StartRecordingAsync();
    }

    [Fact]
    public async Task StartAsync_CancelledWhileOpening_ThrowsOperationCanceledAndStaysIdle()
    {
        // Arrange
        _factory.CreateGate = new TaskCompletionSource();
        using var cancellation = new CancellationTokenSource();
        var start = _sut.StartAsync(MaxDuration, cancellation.Token);

        // Act
        await cancellation.CancelAsync();

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(() => start.WaitAsync(SignalTimeout, Ct));
        _factory.Sessions.ShouldBeEmpty();
        _factory.CreateGate = null;
        await StartRecordingAsync();
    }

    [Fact]
    public async Task StartAsync_OpeningFails_ThrowsTypedExceptionAndStaysIdle()
    {
        // Arrange
        _factory.CreateException = new NoMicrophoneException();

        // Act & Assert
        await Should.ThrowAsync<NoMicrophoneException>(() => _sut.StartAsync(MaxDuration, Ct));
        _sut.IsRecording.ShouldBeFalse();
        _factory.CreateException = null;
        await StartRecordingAsync();
    }

    [Fact]
    public async Task StartAsync_SessionStartFails_ThrowsTypedExceptionAndReleasesSession()
    {
        // Arrange
        _factory.Configure = session => session.StartException = new MicrophoneAccessDeniedException();

        // Act & Assert
        await Should.ThrowAsync<MicrophoneAccessDeniedException>(() => _sut.StartAsync(MaxDuration, Ct));
        _factory.LastSession.IsDisposed.ShouldBeTrue();
        _factory.Configure = null;
        await StartRecordingAsync();
    }

    [Fact]
    public async Task StartAsync_MicrophoneLostBeforeFirstAudio_ThrowsDisconnectedWithoutFailedEvent()
    {
        // Arrange
        var start = _sut.StartAsync(MaxDuration, Ct);
        var session = _factory.LastSession;

        // Act
        session.Fail(new IOException("Device removed."));

        // Assert
        await Should.ThrowAsync<MicrophoneDisconnectedException>(() => start.WaitAsync(SignalTimeout, Ct));
        session.IsDisposed.ShouldBeTrue();
        _failedCount.ShouldBe(0);
        await StartRecordingAsync();
    }

    [Fact]
    public async Task StopAsync_Idle_ThrowsInvalidOperation()
    {
        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(() => _sut.StopAsync());
    }

    [Fact]
    public async Task StopAsync_Recording_ReturnsAudioAndReleasesSession()
    {
        // Arrange
        var session = await StartRecordingAsync();
        session.Deliver(Speech);

        // Act
        var clip = await _sut.StopAsync();

        // Assert
        clip.Samples.ShouldBe([..Speech, ..Speech]);
        session.StopCalls.ShouldBe(1);
        session.IsDisposed.ShouldBeTrue();
        _sut.IsRecording.ShouldBeFalse();
        await Should.ThrowAsync<InvalidOperationException>(() => _sut.StopAsync());
    }

    [Fact]
    public async Task StopAsync_LimitReached_ReturnsKeptAudio()
    {
        // Arrange
        await ReachLimitAsync();

        // Act
        var clip = await _sut.StopAsync();

        // Assert
        clip.Samples.Length.ShouldBe(16);
        _sut.IsRecording.ShouldBeFalse();
    }

    [Fact]
    public async Task StopAsync_Failed_ThrowsStoredErrorOnceThenIsIdle()
    {
        // Arrange
        await LoseMicrophoneAsync();

        // Act & Assert
        await Should.ThrowAsync<MicrophoneDisconnectedException>(() => _sut.StopAsync());
        await Should.ThrowAsync<InvalidOperationException>(() => _sut.StopAsync());
    }

    [Fact]
    public async Task StopAndAbort_WhileStartPending_ThrowInvalidOperation()
    {
        // Arrange
        var start = _sut.StartAsync(MaxDuration, Ct);

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(() => _sut.StopAsync());
        await Should.ThrowAsync<InvalidOperationException>(() => _sut.AbortAsync());
        _factory.LastSession.Deliver(Speech);
        await start.WaitAsync(SignalTimeout, Ct);
        _sut.IsRecording.ShouldBeTrue();
    }

    [Fact]
    public async Task AbortAsync_Idle_HasNoEffect()
    {
        // Act
        await _sut.AbortAsync();

        // Assert
        _factory.Sessions.ShouldBeEmpty();
        await StartRecordingAsync();
    }

    [Fact]
    public async Task AbortAsync_Recording_DiscardsAudioAndReleasesSession()
    {
        // Arrange
        var session = await StartRecordingAsync();

        // Act
        await _sut.AbortAsync();

        // Assert
        session.StopCalls.ShouldBe(1);
        session.IsDisposed.ShouldBeTrue();
        _sut.IsRecording.ShouldBeFalse();
        await Should.ThrowAsync<InvalidOperationException>(() => _sut.StopAsync());
    }

    [Fact]
    public async Task AbortAsync_LimitReached_DiscardsAudio()
    {
        // Arrange
        await ReachLimitAsync();

        // Act
        await _sut.AbortAsync();

        // Assert
        await Should.ThrowAsync<InvalidOperationException>(() => _sut.StopAsync());
    }

    [Fact]
    public async Task AbortAsync_Failed_ClearsErrorWithoutThrowing()
    {
        // Arrange
        await LoseMicrophoneAsync();

        // Act
        await _sut.AbortAsync();

        // Assert
        await Should.ThrowAsync<InvalidOperationException>(() => _sut.StopAsync());
    }

    [Fact]
    public async Task AbortAsync_SessionStopFails_DoesNotThrow()
    {
        // Arrange
        var session = await StartRecordingAsync();
        session.OnStop = () => throw new IOException("Device removed.");

        // Act
        await _sut.AbortAsync();

        // Assert
        session.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task LimitReached_WhileRecording_StopsSessionAndRaisesMaxDurationReachedOnce()
    {
        // Arrange
        var session = await StartRecordingAsync(TimeSpan.FromMilliseconds(1));

        // Act
        session.Deliver(new float[10]);
        session.Deliver(new float[10]);
        await _maxDurationReached.Task.WaitAsync(SignalTimeout, Ct);

        // Assert
        _sut.IsRecording.ShouldBeFalse();
        session.StopCalls.ShouldBe(1);
        session.IsDisposed.ShouldBeTrue();
        _maxDurationReachedCount.ShouldBe(1);
        var clip = await _sut.StopAsync();
        clip.Samples.ShouldBe([..Speech, ..new float[13]]);
        _maxDurationReachedCount.ShouldBe(1);
    }

    [Fact]
    public async Task StopAsync_LimitReachedWhileSessionStillStopping_WaitsForRelease()
    {
        // Arrange
        var stopGate = new TaskCompletionSource();
        var session = await StartRecordingAsync(TimeSpan.FromMilliseconds(1));
        session.StopGate = stopGate;
        session.Deliver(new float[20]);

        // Act
        var stop = _sut.StopAsync();
        var completedWhileStopping = stop.IsCompleted;
        stopGate.SetResult();
        var clip = await stop.WaitAsync(SignalTimeout, Ct);

        // Assert
        completedWhileStopping.ShouldBeFalse();
        clip.Samples.Length.ShouldBe(16);
        session.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task MicrophoneLost_WhileRecording_RaisesFailedOnceOnThreadPoolAndReleasesSession()
    {
        // Arrange
        var session = await StartRecordingAsync();

        // Act
        session.Fail(new IOException("Device removed."));
        var (error, onThreadPool) = await _failed.Task.WaitAsync(SignalTimeout, Ct);

        // Assert
        error.ShouldBeOfType<MicrophoneDisconnectedException>();
        onThreadPool.ShouldBeTrue();
        session.IsDisposed.ShouldBeTrue();
        _sut.IsRecording.ShouldBeFalse();
        session.Fail(new IOException("Device removed."));
        _failedCount.ShouldBe(1);
    }

    [Fact]
    public async Task MicrophoneLost_FailedHandlerThrows_LogsHandlerError()
    {
        // Arrange
        var handlerError = new InvalidOperationException("Handler failed.");
        _sut.Failed += (_, _) => throw handlerError;

        // Act
        await LoseMicrophoneAsync();

        // Assert
        SpinWait.SpinUntil(() => _logger.Entries.Any(entry => entry.Exception == handlerError), SignalTimeout)
            .ShouldBeTrue();
        _logger.Entries.Single(entry => entry.Exception == handlerError).Level.ShouldBe(LogLevel.Error);
    }

    [Fact]
    public async Task StopAsync_MicrophoneLostWhileStopping_ReturnsAudioWithoutFailed()
    {
        // Arrange
        var session = await StartRecordingAsync();
        session.OnStop = () => session.Fail(new IOException("Device removed."));

        // Act
        var clip = await _sut.StopAsync();

        // Assert
        clip.Samples.ShouldBe(Speech);
        _failedCount.ShouldBe(0);
    }

    [Fact]
    public async Task DisposeAsync_Recording_ReleasesSessionAndLaterCallsThrow()
    {
        // Arrange
        var session = await StartRecordingAsync();

        // Act
        await _sut.DisposeAsync();

        // Assert
        session.StopCalls.ShouldBe(1);
        session.IsDisposed.ShouldBeTrue();
        session.Deliver(Speech);
        session.Fail(new IOException("Device removed."));
        _failedCount.ShouldBe(0);
        await Should.ThrowAsync<ObjectDisposedException>(() => _sut.StartAsync(MaxDuration, Ct));
        await Should.ThrowAsync<ObjectDisposedException>(() => _sut.StopAsync());
        await Should.ThrowAsync<ObjectDisposedException>(() => _sut.AbortAsync());
    }

    [Fact]
    public async Task DisposeAsync_WhileStartPending_FailsStartAndReleasesSession()
    {
        // Arrange
        var start = _sut.StartAsync(MaxDuration, Ct);
        var session = _factory.LastSession;

        // Act
        await _sut.DisposeAsync();

        // Assert
        await Should.ThrowAsync<ObjectDisposedException>(() => start.WaitAsync(SignalTimeout, Ct));
        session.IsDisposed.ShouldBeTrue();
        session.Deliver(Speech);
        session.Fail(new IOException("Device removed."));
        _failedCount.ShouldBe(0);
        await Should.ThrowAsync<ObjectDisposedException>(() => _sut.StopAsync());
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        // Arrange
        await StartRecordingAsync();
        await _sut.DisposeAsync();

        // Act & Assert
        await _sut.DisposeAsync();
    }

    private async Task<FakeCaptureSession> StartRecordingAsync(TimeSpan? maxDuration = null)
    {
        var start = _sut.StartAsync(maxDuration ?? MaxDuration, Ct);
        var session = _factory.LastSession;
        session.Deliver(Speech);
        await start.WaitAsync(SignalTimeout, Ct);
        return session;
    }

    private async Task ReachLimitAsync()
    {
        // 1 ms is 16 samples.
        var session = await StartRecordingAsync(TimeSpan.FromMilliseconds(1));
        session.Deliver(new float[20]);
        await _maxDurationReached.Task.WaitAsync(SignalTimeout, Ct);
    }

    private async Task LoseMicrophoneAsync()
    {
        var session = await StartRecordingAsync();
        session.Fail(new IOException("Device removed."));
        await _failed.Task.WaitAsync(SignalTimeout, Ct);
    }
}
