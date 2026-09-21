namespace Pisum.Transcribe.Transcription;

/// <summary>
/// A native call of the speech engine returned an error status.
/// </summary>
internal sealed class NativeEngineException : Exception
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="status">The native status code.</param>
    /// <param name="innerException">The exception of the native binding, or <see langword="null"/>.</param>
    public NativeEngineException(NativeStatus status, Exception? innerException = null)
        : base($"The native speech engine failed with {status}.", innerException)
    {
        Status = status;
    }

    /// <summary>
    /// The native status code.
    /// </summary>
    public NativeStatus Status { get; }
}
