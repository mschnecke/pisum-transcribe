namespace Pisum.Transcribe.Recording;

/// <summary>
/// Receives captured samples.
/// </summary>
/// <param name="samples">16 kHz mono float samples. Valid only during the call.</param>
/// <param name="silent"><see langword="true"/> if Windows marks the packet as silence; the samples are then zero.</param>
internal delegate void SamplesAvailableHandler(ReadOnlySpan<float> samples, bool silent);

/// <summary>
/// One opened microphone stream in 16 kHz mono float, the seam between <see cref="AudioRecorder"/> and the audio API.
/// Dispose it to release the microphone.
/// </summary>
internal interface ICaptureSession : IAsyncDisposable
{
    /// <summary>
    /// Raised on the capture thread for each captured packet. Handlers must return quickly and must not stop or
    /// dispose the session.
    /// </summary>
    event SamplesAvailableHandler? SamplesAvailable;

    /// <summary>
    /// Raised once when capture has ended, with the error, or with <see langword="null"/> when stopped by
    /// <see cref="StopAsync"/>.
    /// </summary>
    event EventHandler<Exception?>? Stopped;

    /// <summary>
    /// Starts capturing.
    /// </summary>
    /// <exception cref="RecordingFailedException">The microphone could not be started.</exception>
    void Start();

    /// <summary>
    /// Stops capturing. Completes at once if capture is not running.
    /// </summary>
    /// <returns>A task that completes when capture has ended.</returns>
    Task StopAsync();
}
