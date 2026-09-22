using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Pisum.Transcribe.Recording;

/// <summary>
/// A capture session on an NAudio <see cref="WasapiRecorder"/>.
/// </summary>
internal sealed class WasapiCaptureSession : ICaptureSession
{
    private const int EAccessDenied = unchecked((int) 0x80070005);
    private const int ENotFound = unchecked((int) 0x80070490);

    private readonly WasapiRecorder _recorder;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Only touched by the capture thread.
    private float[] _silence = [];
    private volatile bool _started;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="recorder">A recorder in 16 kHz mono float, not yet started. Disposed with the session.</param>
    public WasapiCaptureSession(WasapiRecorder recorder)
    {
        _recorder = recorder;
        _recorder.DataAvailable += OnDataAvailable;
        _recorder.RecordingStopped += OnRecordingStopped;
    }

    /// <inheritdoc />
    public event SamplesAvailableHandler? SamplesAvailable;

    /// <inheritdoc />
    public event EventHandler<Exception?>? Stopped;

    /// <summary>
    /// Maps an audio API failure to the error the user sees.
    /// </summary>
    /// <param name="exception">The failure while opening or starting the microphone.</param>
    /// <returns>The error for the user, or <see langword="null"/> if the failure has no specific meaning.</returns>
    public static RecordingFailedException? MapException(Exception exception)
    {
        return exception switch
        {
            RecordingFailedException => null,
            UnauthorizedAccessException or {HResult: EAccessDenied} => new MicrophoneAccessDeniedException(exception),
            {HResult: ENotFound} => new NoMicrophoneException(exception),
            _ => null,
        };
    }

    /// <inheritdoc />
    public void Start()
    {
        try
        {
            _recorder.StartRecording();
            _started = true;
        }
        catch (Exception exception) when (MapException(exception) is { } error)
        {
            throw error;
        }
    }

    /// <inheritdoc />
    public Task StopAsync()
    {
        // Capture that ended by itself, for example because the microphone was lost, is not stopped again.
        if (!_started || _stopped.Task.IsCompleted)
        {
            return Task.CompletedTask;
        }

        _recorder.StopRecording();
        return _stopped.Task;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return _recorder.DisposeAsync();
    }

    private void OnDataAvailable(ReadOnlySpan<byte> buffer,
                                 AudioClientBufferFlags flags,
                                 long devicePosition,
                                 long qpcPosition)
    {
        var samples = MemoryMarshal.Cast<byte, float>(buffer);
        var silent = (flags & AudioClientBufferFlags.Silent) != 0;
        if (silent)
        {
            // Windows asks to treat a silent packet as silence and to ignore its data.
            if (_silence.Length < samples.Length)
            {
                _silence = new float[samples.Length];
            }

            samples = _silence.AsSpan(0, samples.Length);
        }

        SamplesAvailable?.Invoke(samples, silent);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _stopped.TrySetResult();
        Stopped?.Invoke(this, e.Exception);
    }
}
