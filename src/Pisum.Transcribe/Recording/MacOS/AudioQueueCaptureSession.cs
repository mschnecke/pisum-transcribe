namespace Pisum.Transcribe.Recording;

/// <summary>
/// A capture session on AudioQueue that owns its device (design D8 of add-macos-recording): when the default input
/// device changes, the queue moves to the new device within the same session, and when no input device remains, the
/// session ends with <see cref="MicrophoneDisconnectedException"/>. An AudioQueue input alone would stop calling back
/// when its device goes away.
/// </summary>
/// <remarks>
/// The new device's mute state isn't checked on a move, as on Windows, where the mute check runs only at start.
/// </remarks>
internal sealed class AudioQueueCaptureSession : ICaptureSession
{
    private readonly IAudioInput _input;

    // Guards the queue, the device and the state. The move runs on the thread pool, StopAsync on the recorder's thread.
    private readonly Lock _lock = new();
    private IAudioInputQueue? _queue;
    private IDisposable? _watch;
    private uint _device;
    private bool _started;
    private bool _ended;

    /// <summary>
    /// Initializes a new instance and opens its queue on a device, not yet started.
    /// </summary>
    /// <param name="input">The input devices and queues.</param>
    /// <param name="device">The default input device, checked by the factory.</param>
    /// <exception cref="NoMicrophoneException">The device is gone.</exception>
    /// <exception cref="IOException">The queue could not be opened.</exception>
    public AudioQueueCaptureSession(IAudioInput input, uint device)
    {
        _input = input;
        _device = device;
        _queue = input.OpenQueue(device, OnSamplesAvailable);
    }

    /// <inheritdoc />
    public event SamplesAvailableHandler? SamplesAvailable;

    /// <inheritdoc />
    public event EventHandler<Exception?>? Stopped;

    /// <inheritdoc />
    public void Start()
    {
        lock (_lock)
        {
            // Watched first, so a change while the queue starts isn't missed.
            _watch = _input.WatchDefaultDevice(OnDefaultDeviceChanged);
            _queue!.Start();
            _started = true;
        }
    }

    /// <inheritdoc />
    public Task StopAsync()
    {
        lock (_lock)
        {
            // Capture that ended by itself, because the microphone was lost, isn't stopped again.
            if (!_started || _ended)
            {
                return Task.CompletedTask;
            }

            Release();
        }

        Stopped?.Invoke(this, null);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            Release();
        }

        return ValueTask.CompletedTask;
    }

    // Call inside the lock. The listener is removed before the queue, and each frees its own handle last.
    private void Release()
    {
        _ended = true;
        _watch?.Dispose();
        _watch = null;
        _queue?.Dispose();
        _queue = null;
    }

    private void OnDefaultDeviceChanged()
    {
        // Off CoreAudio's notification thread, which must not wait for a queue to stop or open.
        _ = Task.Run(FollowDefaultDevice);
    }

    private void FollowDefaultDevice()
    {
        Exception? error = null;
        lock (_lock)
        {
            if (_ended)
            {
                return;
            }

            var device = _input.GetDefaultDevice();
            if (device == _device)
            {
                return;
            }

            if (device == CoreAudio.UnknownObject)
            {
                error = new MicrophoneDisconnectedException();
            }
            else
            {
                try
                {
                    _queue?.Dispose();
                    _queue = null;
                    _device = device;
                    _queue = _input.OpenQueue(device, OnSamplesAvailable);
                    _queue.Start();
                }
                catch (Exception exception)
                {
                    error = new MicrophoneDisconnectedException(exception);
                }
            }

            if (error is not null)
            {
                Release();
            }
        }

        if (error is not null)
        {
            Stopped?.Invoke(this, error);
        }
    }

    private void OnSamplesAvailable(ReadOnlySpan<float> samples, bool silent)
    {
        SamplesAvailable?.Invoke(samples, silent);
    }
}
