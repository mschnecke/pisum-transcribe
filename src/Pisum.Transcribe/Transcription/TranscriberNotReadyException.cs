namespace Pisum.Transcribe.Transcription;

/// <summary>
/// A transcription request arrived while the engine was not <see cref="TranscriberStatus.Ready"/>.
/// </summary>
internal sealed class TranscriberNotReadyException : Exception
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="status">The status when the request arrived.</param>
    public TranscriberNotReadyException(TranscriberStatus status)
        : base(status switch
        {
            TranscriberStatus.Loading => "The model is still loading.",
            TranscriberStatus.Failed => "The model failed to load.",
            _ => "No model is loaded.",
        })
    {
        Status = status;
    }

    /// <summary>
    /// The status when the request arrived.
    /// </summary>
    public TranscriberStatus Status { get; }
}
