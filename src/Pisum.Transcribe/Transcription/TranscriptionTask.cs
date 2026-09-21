namespace Pisum.Transcribe.Transcription;

/// <summary>
/// What the transcription engine produces from speech.
/// </summary>
internal enum TranscriptionTask
{
    /// <summary>
    /// Text in the target language, translated from the source language.
    /// </summary>
    Translate,

    /// <summary>
    /// Text in the source language. The target language is ignored.
    /// </summary>
    Transcribe,
}
