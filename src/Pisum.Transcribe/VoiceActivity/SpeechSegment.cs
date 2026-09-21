namespace Pisum.Transcribe.VoiceActivity;

/// <summary>
/// A stretch of detected speech in a recording.
/// </summary>
/// <param name="StartSample">The index of the first sample of the speech.</param>
/// <param name="EndSample">The index after the last sample of the speech.</param>
internal readonly record struct SpeechSegment(int StartSample, int EndSample);
