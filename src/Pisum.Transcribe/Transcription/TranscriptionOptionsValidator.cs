using Pisum.Transcribe.SpeechModels;

namespace Pisum.Transcribe.Transcription;

/// <summary>
/// Checks transcription options against the languages of a catalog model.
/// </summary>
internal static class TranscriptionOptionsValidator
{
    private const string English = "en";

    /// <summary>
    /// Checks that the model supports the options. The source language must be one of the model's languages. For
    /// <see cref="TranscriptionTask.Translate"/>, the target language must be one of them too, differ from the source,
    /// and one of the two must be English. For <see cref="TranscriptionTask.Transcribe"/>, the target is ignored.
    /// </summary>
    /// <param name="model">The catalog model.</param>
    /// <param name="options">The options to check.</param>
    /// <returns>Why the options are not supported, or <see langword="null"/> if they are.</returns>
    public static string? Validate(SpeechModel model, TranscriptionOptions options)
    {
        var source = options.SourceLanguage;
        if (!model.Languages.Contains(source))
        {
            return $"{model.DisplayName} does not support the source language \"{source}\".";
        }

        if (options.Task == TranscriptionTask.Transcribe)
        {
            return null;
        }

        var target = options.TargetLanguage;
        if (!model.Languages.Contains(target))
        {
            return $"{model.DisplayName} does not support the target language \"{target}\".";
        }

        if (source == target)
        {
            return "Translation needs different source and target languages.";
        }

        if (source != English && target != English)
        {
            return "Translation needs English as the source or the target language.";
        }

        return null;
    }
}
