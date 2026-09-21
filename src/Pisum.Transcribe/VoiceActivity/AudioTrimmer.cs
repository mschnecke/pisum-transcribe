using Pisum.Transcribe.Recording;

namespace Pisum.Transcribe.VoiceActivity;

/// <summary>
/// Cuts the silence before and after the speech from a recording. Pauses inside the speech are kept.
/// </summary>
internal static class AudioTrimmer
{
    /// <summary>
    /// The audio kept before the first and after the last speech: 300 ms at <see cref="AudioClip.SampleRate"/>.
    /// </summary>
    public const int DefaultPadSamples = AudioClip.SampleRate * 300 / 1000;

    /// <summary>
    /// Keeps the audio from <paramref name="padSamples"/> before the first speech to <paramref name="padSamples"/> after
    /// the last speech, clamped to the recording.
    /// </summary>
    /// <param name="samples">The recording.</param>
    /// <param name="segments">The speech in the recording, in ascending order.</param>
    /// <param name="padSamples">The number of samples kept on each side of the speech.</param>
    /// <returns>
    /// The trimmed audio, <paramref name="samples"/> itself if nothing is cut, or <see langword="null"/> if there is no
    /// speech.
    /// </returns>
    public static float[]? Trim(float[] samples, IReadOnlyList<SpeechSegment> segments,
                                int padSamples = DefaultPadSamples)
    {
        if (segments.Count == 0)
        {
            return null;
        }

        var start = Math.Max(0, segments[0].StartSample - padSamples);
        var end = Math.Min(samples.Length, segments[^1].EndSample + padSamples);
        return start == 0 && end == samples.Length ? samples : samples[start..end];
    }
}
