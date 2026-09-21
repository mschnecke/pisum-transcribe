namespace Pisum.Transcribe.Recording;

/// <summary>
/// The default recording device is muted in Windows.
/// </summary>
internal sealed class MicrophoneMutedException : RecordingFailedException
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public MicrophoneMutedException()
        : base("The microphone is muted. Unmute the microphone, for example with the microphone key, and try again.",
            null)
    {
    }
}
