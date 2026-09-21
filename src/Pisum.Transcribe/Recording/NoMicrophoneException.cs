namespace Pisum.Transcribe.Recording;

/// <summary>
/// No recording device is connected or enabled.
/// </summary>
internal sealed class NoMicrophoneException : RecordingFailedException
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="innerException">The underlying failure, or <see langword="null"/>.</param>
    public NoMicrophoneException(Exception? innerException = null)
        : base("No microphone found. Connect or turn on a microphone and try again.", innerException)
    {
    }
}
