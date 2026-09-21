using Pisum.Transcribe.Transcription;

namespace Pisum.Transcribe.Settings;

/// <summary>
/// The transcription settings, saved as the <c>transcription</c> section.
/// </summary>
/// <param name="Backend">The compute backend to load the model on.</param>
/// <param name="Task">Whether speech is translated or transcribed.</param>
/// <param name="SourceLanguage">The spoken language as an ISO 639-1 code.</param>
/// <param name="TargetLanguage">The output language of <see cref="TranscriptionTask.Translate"/> as an ISO 639-1 code.</param>
internal sealed record TranscriptionSettings(
    BackendPreference Backend = BackendPreference.Auto,
    TranscriptionTask Task = TranscriptionTask.Translate,
    string SourceLanguage = "de",
    string TargetLanguage = "en");
