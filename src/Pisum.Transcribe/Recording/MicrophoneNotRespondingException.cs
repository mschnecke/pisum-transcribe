namespace Pisum.Transcribe.Recording;

/// <summary>
/// The microphone was opened but delivered no audio in time.
/// </summary>
internal sealed class MicrophoneNotRespondingException : RecordingFailedException
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public MicrophoneNotRespondingException()
        : base("The microphone is not responding. Check that it is connected and working, and try again.", null)
    {
    }
}
