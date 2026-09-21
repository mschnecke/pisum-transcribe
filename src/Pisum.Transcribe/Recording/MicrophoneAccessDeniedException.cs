namespace Pisum.Transcribe.Recording;

/// <summary>
/// The Windows privacy settings deny the application microphone access.
/// </summary>
internal sealed class MicrophoneAccessDeniedException : RecordingFailedException
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="innerException">The underlying failure, or <see langword="null"/>.</param>
    public MicrophoneAccessDeniedException(Exception? innerException = null)
        : base(
            "Microphone access is blocked. In Settings › Privacy & security › Microphone, allow microphone access, " +
            "access for desktop apps, and access for Pisum Transcribe.", innerException)
    {
    }
}
