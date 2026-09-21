namespace Pisum.Transcribe.Transcription;

/// <summary>
/// The state of the transcription engine.
/// </summary>
internal enum TranscriberStatus
{
    /// <summary>
    /// No model is loaded.
    /// </summary>
    NotLoaded,

    /// <summary>
    /// A model is loading and warming up.
    /// </summary>
    Loading,

    /// <summary>
    /// A model is loaded and accepts transcription requests.
    /// </summary>
    Ready,

    /// <summary>
    /// Loading failed, or the backend failed with no fallback left.
    /// </summary>
    Failed,
}
