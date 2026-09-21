namespace Pisum.Transcribe.Settings;

/// <summary>
/// The voice activity detection settings, saved as the <c>voiceActivity</c> section.
/// </summary>
/// <param name="Enabled">
/// Whether silence around the speech is trimmed before transcription and recordings without speech are not transcribed.
/// </param>
internal sealed record VoiceActivitySettings(bool Enabled = true);
