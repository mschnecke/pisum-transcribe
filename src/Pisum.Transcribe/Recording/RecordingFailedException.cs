namespace Pisum.Transcribe.Recording;

/// <summary>
/// A recording could not start or was lost. The message is fit for the user.
/// </summary>
internal abstract class RecordingFailedException : Exception
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="message">The message for the user.</param>
    /// <param name="innerException">The underlying failure, or <see langword="null"/>.</param>
    protected RecordingFailedException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
