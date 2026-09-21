namespace Pisum.Transcribe.Recording;

/// <summary>
/// The microphone was lost during a recording, and no other recording device took over.
/// </summary>
internal sealed class MicrophoneDisconnectedException : RecordingFailedException
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="innerException">The capture failure, or <see langword="null"/>.</param>
    public MicrophoneDisconnectedException(Exception? innerException = null)
        : base("The microphone was disconnected, and the recording was discarded.", innerException)
    {
    }
}
