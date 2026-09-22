namespace Pisum.Transcribe.Tray;

/// <summary>
/// The status that the tray icon shows.
/// </summary>
internal enum TrayStatus
{
    /// <summary>
    /// Ready to dictate.
    /// </summary>
    Ready,

    /// <summary>
    /// A dictation is recording.
    /// </summary>
    Recording,

    /// <summary>
    /// A dictation is transcribed.
    /// </summary>
    Transcribing,

    /// <summary>
    /// No model, loading or failed.
    /// </summary>
    Unavailable,
}
