namespace Pisum.Transcribe.Recording;

/// <summary>
/// The Windows privacy settings or the macOS microphone permission deny the application microphone access.
/// </summary>
internal sealed class MicrophoneAccessDeniedException : RecordingFailedException
{
#if WINDOWS
    private const string Text =
        "Microphone access is blocked. In Settings › Privacy & security › Microphone, allow microphone access, " +
        "access for desktop apps, and access for Pisum Transcribe.";
#else
    private const string Text =
        "Microphone access is blocked. In System Settings → Privacy & Security → Microphone, allow access for " +
        "Pisum Transcribe.";
#endif

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="innerException">The underlying failure, or <see langword="null"/>.</param>
    public MicrophoneAccessDeniedException(Exception? innerException = null)
        : base(Text, innerException)
    {
    }
}
