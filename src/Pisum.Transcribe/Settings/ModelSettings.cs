using Pisum.Transcribe.SpeechModels;

namespace Pisum.Transcribe.Settings;

/// <summary>
/// The speech model settings, saved as the <c>model</c> section.
/// </summary>
/// <param name="SelectedModelId">
/// The identifier of the selected catalog model. Resolve it with <see cref="ModelCatalog.Resolve"/>, which treats an
/// unknown identifier as the default.
/// </param>
internal sealed record ModelSettings(string SelectedModelId = ModelCatalog.DefaultModelId);
