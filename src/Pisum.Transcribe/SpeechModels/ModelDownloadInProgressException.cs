namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// A download of a model was started while another download of the same model was running.
/// </summary>
internal sealed class ModelDownloadInProgressException : Exception
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="modelId">The identifier of the model that is already downloading.</param>
    public ModelDownloadInProgressException(string modelId)
        : base($"Model {modelId} is already downloading.")
    {
        ModelId = modelId;
    }

    /// <summary>
    /// The identifier of the model that is already downloading.
    /// </summary>
    public string ModelId { get; }
}
