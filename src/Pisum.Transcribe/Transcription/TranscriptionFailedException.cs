namespace Pisum.Transcribe.Transcription;

/// <summary>
/// The native engine failed a transcription. The message is fit for the user; the status code belongs in the log.
/// </summary>
internal sealed class TranscriptionFailedException : Exception
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="statusCode">The native status code.</param>
    /// <param name="innerException">The native failure.</param>
    public TranscriptionFailedException(NativeStatus statusCode, Exception innerException)
        : base("Transcription failed.", innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// The native status code.
    /// </summary>
    public NativeStatus StatusCode { get; }
}
