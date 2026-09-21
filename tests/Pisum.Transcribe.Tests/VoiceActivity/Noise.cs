using Pisum.Transcribe.VoiceActivity;

namespace Pisum.Transcribe.Tests.VoiceActivity;

/// <summary>
/// Reproducible white noise, a stand-in for background noise such as a fan.
/// </summary>
internal static class Noise
{
    /// <summary>
    /// Creates uniform white noise between <c>-amplitude</c> and <c>amplitude</c>.
    /// </summary>
    public static float[] Create(int sampleCount, float amplitude)
    {
        var random = new Random(4711);
        var samples = new float[sampleCount];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float) ((random.NextDouble() * 2 - 1) * amplitude);
        }

        return samples;
    }

    /// <summary>
    /// Creates white noise as consecutive model windows.
    /// </summary>
    public static float[][] Windows(int count, float amplitude)
    {
        return Create(count * SileroVadModel.WindowSize, amplitude).Chunk(SileroVadModel.WindowSize).ToArray();
    }
}
