using Microsoft.Extensions.Logging;

namespace Pisum.Transcribe.Recording;

/// <summary>
/// Records through a new <see cref="ICaptureSession"/> per recording, so the microphone is closed while idle.
/// </summary>
/// <remarks>
/// A start completes on the first packet that is neither marked as silence by Windows nor all zeros, or fails after
/// <see cref="StartTimeout"/>. When the limit is reached or the microphone is lost, the session is released on the thread
/// pool, never inside its own capture callback, and the event follows once it is released. Once a stop or abort has
/// begun, the session is detached, so a late session error changes nothing. A lock guards the state and the samples;
/// session calls and awaits run outside it.
/// </remarks>
internal sealed class AudioRecorder : IAudioRecorder, IAsyncDisposable
{
    /// <summary>
    /// How long a start waits for the first audio that is not silence.
    /// </summary>
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(3);

    private readonly ICaptureSessionFactory _sessionFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AudioRecorder> _logger;
    private readonly CancellationTokenSource _disposing = new();
    private readonly Lock _lock = new();

    // Guarded by _lock. _session is the attached session, set only while Starting or Recording; events of any other
    // session are ignored.
    private State _state;
    private int _recordingId;
    private ICaptureSession? _session;
    private SampleAccumulator? _samples;
    private TaskCompletionSource? _audioFlowing;
    private RecordingFailedException? _error;
    private Task _sessionRelease = Task.CompletedTask;
    private Task _pendingStart = Task.CompletedTask;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="sessionFactory">Opens the capture sessions.</param>
    /// <param name="timeProvider">The time provider for the start timeout.</param>
    /// <param name="logger">The logger.</param>
    public AudioRecorder(ICaptureSessionFactory sessionFactory,
                         TimeProvider timeProvider,
                         ILogger<AudioRecorder> logger)
    {
        _sessionFactory = sessionFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler? MaxDurationReached;

    /// <inheritdoc />
    public event EventHandler<RecordingFailedException>? Failed;

    private enum State
    {
        Idle,
        Starting,
        Recording,
        LimitReached,
        Failed,
    }

    /// <inheritdoc />
    public bool IsRecording
    {
        get
        {
            lock (_lock)
            {
                return _state == State.Recording;
            }
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(TimeSpan maxDuration, CancellationToken cancellationToken)
    {
        var maxCount = SampleAccumulator.MaxCountFor(maxDuration);
        var audioFlowing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task previousRelease;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_state is not (State.Idle or State.Failed))
            {
                throw new InvalidOperationException($"A recording cannot start while the recorder is {_state}.");
            }

            _state = State.Starting;
            _recordingId++;
            _error = null;
            _samples = new SampleAccumulator(maxCount);
            _audioFlowing = audioFlowing;
            _pendingStart = startDone.Task;
            previousRelease = _sessionRelease;
        }

        ICaptureSession? session = null;
        try
        {
            // The timeout also covers opening the session, so a device that hangs during activation fails the start.
            using var timeout = new CancellationTokenSource(StartTimeout, _timeProvider);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token,
                _disposing.Token);
            using var registration = cancellation.Token.Register(() => FailStart(audioFlowing, CancellationReason()));

            try
            {
                // Releases the microphone of a failed or limited recording before opening it again.
                await previousRelease.ConfigureAwait(false);
                session = await _sessionFactory.CreateAsync(cancellation.Token).ConfigureAwait(false);
                if (Attach(audioFlowing, session))
                {
                    session.Start();
                }
            }
            catch (Exception exception)
            {
                FailStart(audioFlowing,
                    exception is OperationCanceledException && cancellation.IsCancellationRequested
                        ? CancellationReason()
                        : exception);
            }

            await audioFlowing.Task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (exception is RecordingFailedException)
            {
                _logger.LogWarning(exception, "The recording could not start");
            }

            if (session is not null)
            {
                await ReleaseAsync(session).ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            startDone.SetResult();
        }

        Exception CancellationReason()
        {
            return _disposing.IsCancellationRequested ? new ObjectDisposedException(nameof(AudioRecorder)) :
                cancellationToken.IsCancellationRequested ? new OperationCanceledException(cancellationToken) :
                new MicrophoneNotRespondingException();
        }
    }

    /// <inheritdoc />
    public async Task<AudioClip> StopAsync()
    {
        float[]? samples = null;
        RecordingFailedException? error = null;
        Task release;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            switch (_state)
            {
                case State.Recording:
                    QueueRelease(_session!);
                    samples = _samples!.ToArray();
                    break;
                case State.LimitReached:
                    samples = _samples!.ToArray();
                    break;
                case State.Failed:
                    error = _error!;
                    break;
                default:
                    throw new InvalidOperationException($"No recording can stop while the recorder is {_state}.");
            }

            ResetToIdle();
            release = _sessionRelease;
        }

        await release.ConfigureAwait(false);
        return error is null ? new AudioClip(samples!) : throw error;
    }

    /// <inheritdoc />
    public async Task AbortAsync()
    {
        Task release;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            switch (_state)
            {
                case State.Idle:
                    return;
                case State.Starting:
                    throw new InvalidOperationException("No recording can abort while the recorder is Starting.");
                case State.Recording:
                    QueueRelease(_session!);
                    break;
            }

            _samples?.Clear();
            ResetToIdle();
            release = _sessionRelease;
        }

        await release.ConfigureAwait(false);
    }

    /// <summary>
    /// Aborts in any state, including a pending start, and waits until the microphone is released. Later calls throw
    /// <see cref="ObjectDisposedException"/>, and no events are raised afterwards.
    /// </summary>
    /// <returns>A task that completes when the microphone is released.</returns>
    public async ValueTask DisposeAsync()
    {
        Task pendingStart;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pendingStart = _pendingStart;
        }

        // Fails a pending start, which then releases its session itself.
        await _disposing.CancelAsync().ConfigureAwait(false);
        await pendingStart.ConfigureAwait(false);

        Task release;
        lock (_lock)
        {
            if (_session is { } session)
            {
                QueueRelease(session);
            }

            _samples?.Clear();
            ResetToIdle();
            release = _sessionRelease;
        }

        await release.ConfigureAwait(false);
    }

    private bool Attach(TaskCompletionSource audioFlowing, ICaptureSession session)
    {
        session.SamplesAvailable += (samples, silent) => OnSamplesAvailable(session, samples, silent);
        session.Stopped += (_, exception) => OnStopped(session, exception);
        lock (_lock)
        {
            if (_audioFlowing != audioFlowing || _state != State.Starting)
            {
                // The start has already failed, for example because it was cancelled.
                return false;
            }

            _session = session;
            return true;
        }
    }

    private void FailStart(TaskCompletionSource audioFlowing, Exception exception)
    {
        lock (_lock)
        {
            // Only one of the first audio and a failure ends a start.
            if (_audioFlowing != audioFlowing || _state != State.Starting)
            {
                return;
            }

            _audioFlowing = null;
            ResetToIdle();
        }

        if (exception is OperationCanceledException cancelled)
        {
            audioFlowing.TrySetCanceled(cancelled.CancellationToken);
        }
        else
        {
            audioFlowing.TrySetException(exception);
        }
    }

    private void OnSamplesAvailable(ICaptureSession session, ReadOnlySpan<float> samples, bool silent)
    {
        TaskCompletionSource? audioFlowing = null;
        lock (_lock)
        {
            if (!ReferenceEquals(_session, session))
            {
                return;
            }

            if (_state == State.Starting)
            {
                // Digital silence while the device wakes up or a Bluetooth headset switches its profile. Some microphone
                // arrays send it as zeros without the silent flag. Silence after the first audio is a real pause and is
                // kept.
                if (silent || samples.IndexOfAnyExcept(0f) < 0)
                {
                    return;
                }

                _state = State.Recording;
                audioFlowing = _audioFlowing;
                _audioFlowing = null;
            }

            if (_samples!.Append(samples))
            {
                _state = State.LimitReached;
                _session = null;
                var recordingId = _recordingId;
                QueueRelease(session, () => RaiseMaxDurationReached(recordingId));
            }
        }

        audioFlowing?.TrySetResult();
    }

    private void OnStopped(ICaptureSession session, Exception? exception)
    {
        TaskCompletionSource? audioFlowing = null;
        lock (_lock)
        {
            // A session stopped by request is already detached.
            if (!ReferenceEquals(_session, session))
            {
                return;
            }

            if (_state == State.Starting)
            {
                audioFlowing = _audioFlowing;
            }
            else
            {
                var error = new MicrophoneDisconnectedException(exception);
                _samples!.Clear();
                ResetToIdle();
                _state = State.Failed;
                _error = error;
                var recordingId = _recordingId;
                QueueRelease(session, () => RaiseFailed(recordingId, error));
            }
        }

        _logger.LogWarning(exception, "The microphone was lost");
        if (audioFlowing is not null)
        {
            FailStart(audioFlowing, new MicrophoneDisconnectedException(exception));
        }
    }

    // Call inside the lock.
    private void ResetToIdle()
    {
        _state = State.Idle;
        _session = null;
        _samples = null;
        _error = null;
    }

    // Call inside the lock. Runs the release on the thread pool, then raiseEvent, and logs a handler's exception.
    private void QueueRelease(ICaptureSession session, Action? raiseEvent = null)
    {
        var release = Task.Run(() => ReleaseAsync(session));
        _sessionRelease = release;
        if (raiseEvent is not null)
        {
            // Not part of _sessionRelease, so a handler that calls StopAsync does not wait for itself.
            release.ContinueWith(_ =>
            {
                try
                {
                    raiseEvent();
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "A recorder event handler failed");
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    private async Task ReleaseAsync(ICaptureSession session)
    {
        try
        {
            await session.StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Stopping the microphone failed");
        }

        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Releasing the microphone failed");
        }
    }

    private void RaiseMaxDurationReached(int recordingId)
    {
        lock (_lock)
        {
            if (_disposed || _recordingId != recordingId || _state != State.LimitReached)
            {
                return;
            }
        }

        _logger.LogInformation("The recording reached its maximum duration");
        MaxDurationReached?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseFailed(int recordingId, RecordingFailedException error)
    {
        lock (_lock)
        {
            if (_disposed || _recordingId != recordingId || _state != State.Failed)
            {
                return;
            }
        }

        Failed?.Invoke(this, error);
    }
}
