using System.Globalization;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Transcription;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// A language as the language pickers show it.
/// </summary>
/// <param name="Code">The ISO 639-1 code, as stored in the settings.</param>
/// <param name="Name">The English name, such as <c>German</c>.</param>
internal sealed record LanguageOption(string Code, string Name);

/// <summary>
/// The languages the pickers offer for a model, sorted by name.
/// </summary>
/// <param name="Sources">The source languages: the model's languages.</param>
/// <param name="Targets">
/// The target languages of <see cref="TranscriptionTask.Translate"/>: English for a non-English source, and the model's
/// non-English languages for an English source. Empty for <see cref="TranscriptionTask.Transcribe"/>.
/// </param>
internal sealed record LanguageOptions(IReadOnlyList<LanguageOption> Sources, IReadOnlyList<LanguageOption> Targets)
{
    private const string English = "en";

    /// <summary>
    /// Gets the options for a model, task and source language.
    /// </summary>
    /// <param name="model">The catalog model.</param>
    /// <param name="task">The task.</param>
    /// <param name="sourceLanguage">The chosen source language as an ISO 639-1 code.</param>
    /// <returns>The source and target options.</returns>
    public static LanguageOptions For(SpeechModel model, TranscriptionTask task, string sourceLanguage)
    {
        var sources = ToOptions(model.Languages);
        if (task == TranscriptionTask.Transcribe)
        {
            return new LanguageOptions(sources, []);
        }

        var targets = sourceLanguage == English
            ? ToOptions(model.Languages.Where(code => code != English))
            : ToOptions(model.Languages.Where(code => code == English));
        return new LanguageOptions(sources, targets);
    }

    private static List<LanguageOption> ToOptions(IEnumerable<string> codes)
    {
        return codes.Select(code => new LanguageOption(code, CultureInfo.GetCultureInfo(code).EnglishName))
            .OrderBy(option => option.Name)
            .ToList();
    }
}
