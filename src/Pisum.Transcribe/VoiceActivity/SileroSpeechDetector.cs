using Pisum.Transcribe.Recording;

namespace Pisum.Transcribe.VoiceActivity;

/// <summary>
/// Turns the speech probabilities of consecutive <see cref="SileroVadModel.WindowSize"/>-sample windows into speech
/// segments. A segment starts at a window at or above <see cref="Threshold"/> and ends once the probability stays
/// below <see cref="NegativeThreshold"/> for <see cref="MinSilenceSamples"/>. Segments shorter than
/// <see cref="MinSpeechSamples"/> are dropped.
/// </summary>
/// <remarks>
/// Ported from the segment logic of <c>SileroVadDetector.cs</c> in <c>snakers4/silero-vad</c>,
/// <c>examples/csharp</c> (MIT). The maximum speech length and the speech padding are left out: the segments are not
/// split, and <see cref="AudioTrimmer"/> adds the padding.
/// </remarks>
internal static class SileroSpeechDetector
{
    /// <summary>
    /// The probability from which a window counts as speech.
    /// </summary>
    public const float Threshold = 0.5f;

    /// <summary>
    /// The probability below which a window counts as silence.
    /// </summary>
    public const float NegativeThreshold = Threshold - 0.15f;

    /// <summary>
    /// Speech shorter than this, 250 ms, is dropped.
    /// </summary>
    public const int MinSpeechSamples = AudioClip.SampleRate * 250 / 1000;

    /// <summary>
    /// Silence shorter than this, 100 ms, does not end a segment.
    /// </summary>
    public const int MinSilenceSamples = AudioClip.SampleRate * 100 / 1000;

    /// <summary>
    /// Finds the speech segments.
    /// </summary>
    /// <param name="probabilities">The speech probability of each window, in order.</param>
    /// <param name="sampleCount">The number of samples in the recording.</param>
    /// <returns>The speech segments in ascending order.</returns>
    public static IReadOnlyList<SpeechSegment> FindSegments(ReadOnlySpan<float> probabilities, int sampleCount)
    {
        const int windowSize = SileroVadModel.WindowSize;
        var segments = new List<SpeechSegment>();
        var triggered = false;
        var start = 0;
        var tempEnd = 0;

        for (var i = 0; i < probabilities.Length; i++)
        {
            var probability = probabilities[i];
            var position = windowSize * i;
            if (probability >= Threshold && tempEnd != 0)
            {
                tempEnd = 0;
            }

            if (probability >= Threshold && !triggered)
            {
                triggered = true;
                start = position;
                continue;
            }

            if (probability < NegativeThreshold && triggered)
            {
                if (tempEnd == 0)
                {
                    tempEnd = position;
                }

                if (position - tempEnd < MinSilenceSamples)
                {
                    continue;
                }

                if (tempEnd - start > MinSpeechSamples)
                {
                    segments.Add(new SpeechSegment(start, tempEnd));
                }

                tempEnd = 0;
                triggered = false;
            }
        }

        // Speech that lasts to the end ends with the last window. A zero-padded last window is clamped to the recording.
        if (triggered && sampleCount - start > MinSpeechSamples)
        {
            segments.Add(new SpeechSegment(start, Math.Min(probabilities.Length * windowSize, sampleCount)));
        }

        return segments;
    }
}
