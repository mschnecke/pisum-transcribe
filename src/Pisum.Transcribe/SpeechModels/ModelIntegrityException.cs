namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// A downloaded model does not match the catalog: its size or its SHA-256 hash differs.
/// </summary>
internal sealed class ModelIntegrityException : Exception
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="message">What did not match.</param>
    public ModelIntegrityException(string message)
        : base(message)
    {
    }
}
