using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Pisum.Transcribe.Recording;

/// <summary>
/// Opens <see cref="WasapiCaptureSession"/>s that follow the Windows default recording device.
/// </summary>
/// <remarks>
/// In shared mode, Windows converts the device format to 16 kHz mono float, so no resampling happens here.
/// </remarks>
internal sealed class WasapiCaptureSessionFactory : ICaptureSessionFactory
{
    private const int BufferMilliseconds = 20;

    /// <inheritdoc />
    public Task<ICaptureSession> CreateAsync(CancellationToken cancellationToken)
    {
        // Off the UI thread, so NAudio does not marshal RecordingStopped to the WPF dispatcher.
        return Task.Run(() => CreateCoreAsync(cancellationToken), cancellationToken);
    }

    private static async Task<ICaptureSession> CreateCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            ThrowIfNoMicrophoneOrMuted();

            var build = new WasapiRecorderBuilder()
                .WithDefaultDeviceStreamRouting()
                .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(AudioClip.SampleRate, 1))
                .WithBufferLength(BufferMilliseconds)
                .BuildAsync();
            try
            {
                return new WasapiCaptureSession(await build.WaitAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                // Activation cannot be cancelled, so the recorder is released once it exists.
                _ = build.ContinueWith(task => task.Result.Dispose(), CancellationToken.None,
                    TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
                throw;
            }
        }
        catch (Exception exception) when (WasapiCaptureSession.MapException(exception) is { } error)
        {
            throw error;
        }
    }

    private static void ThrowIfNoMicrophoneOrMuted()
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!enumerator.TryGetDefaultAudioEndpoint(DataFlow.Capture, Role.Console, out var device))
        {
            throw new NoMicrophoneException();
        }

        // Checked only at start: opening a muted microphone would record silence and show it as in use.
        using (device)
        {
            if (device.AudioEndpointVolume.Mute)
            {
                throw new MicrophoneMutedException();
            }
        }
    }
}
