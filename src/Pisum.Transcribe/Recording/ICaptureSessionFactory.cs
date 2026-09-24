namespace Pisum.Transcribe.Recording;

/// <summary>
/// Opens capture sessions on the Windows default recording device or the macOS default input device.
/// </summary>
internal interface ICaptureSessionFactory
{
    /// <summary>
    /// Opens a session in 16 kHz mono float, not yet started.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel opening. A session opened anyway is disposed.</param>
    /// <returns>The session.</returns>
    /// <exception cref="RecordingFailedException">The microphone could not be opened.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<ICaptureSession> CreateAsync(CancellationToken cancellationToken);
}
