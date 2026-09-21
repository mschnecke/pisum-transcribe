namespace Pisum.Transcribe.Transcription;

/// <summary>
/// The task and languages of a transcription request.
/// </summary>
/// <param name="Task">Whether speech is translated or transcribed.</param>
/// <param name="SourceLanguage">The spoken language as an ISO 639-1 code.</param>
/// <param name="TargetLanguage">
/// The output language of <see cref="TranscriptionTask.Translate"/> as an ISO 639-1 code. Ignored for
/// <see cref="TranscriptionTask.Transcribe"/>.
/// </param>
internal sealed record TranscriptionOptions(TranscriptionTask Task, string SourceLanguage, string TargetLanguage);
