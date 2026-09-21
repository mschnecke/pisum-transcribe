namespace Pisum.Transcribe.Transcription;

/// <summary>
/// The text of a transcription request.
/// </summary>
/// <param name="Text">The transcribed or translated text. Never log it.</param>
/// <param name="AudioDuration">The duration of the audio input.</param>
/// <param name="ProcessingTime">The time the engine spent on the request, without the time it waited in the queue.</param>
internal sealed record TranscriptionResult(string Text, TimeSpan AudioDuration, TimeSpan ProcessingTime);
