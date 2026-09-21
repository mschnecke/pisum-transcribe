namespace Pisum.Transcribe.Transcription;

/// <summary>
/// The selected model does not support the requested language combination.
/// </summary>
internal sealed class LanguageNotSupportedException : Exception
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="message">Which language or combination is not supported.</param>
    public LanguageNotSupportedException(string message)
        : base(message)
    {
    }
}
