namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// A catalog model as the setup and settings windows list it.
/// </summary>
/// <param name="Model">The catalog model.</param>
/// <param name="SizeText">The download size, such as <c>1.07 GB</c>.</param>
/// <param name="LanguagesText">The supported languages as English names.</param>
internal sealed record ModelOption(SpeechModel Model, string SizeText, string LanguagesText)
{
    /// <summary>
    /// Describes a catalog model.
    /// </summary>
    /// <param name="model">The catalog model.</param>
    /// <returns>The model with its formatted size and languages.</returns>
    public static ModelOption Create(SpeechModel model)
    {
        return new ModelOption(model, ModelText.FormatSize(model.SizeBytes), ModelText.FormatLanguages(model.Languages));
    }
}
