namespace Pisum.Transcribe.Transcription;

/// <summary>
/// The output of one native run.
/// </summary>
/// <param name="Text">The text. Never log it.</param>
/// <param name="Truncated">Whether the run stopped at the model's output limit, so <paramref name="Text"/> is partial.</param>
internal sealed record NativeRunOutput(string Text, bool Truncated);
