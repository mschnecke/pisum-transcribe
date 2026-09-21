namespace Pisum.Transcribe.Recording;

/// <summary>
/// Recorded audio, kept in memory only.
/// </summary>
/// <param name="Samples">Mono 32-bit float samples at <see cref="SampleRate"/> in the range [-1, 1].</param>
internal sealed record AudioClip(float[] Samples)
{
    /// <summary>
    /// The sample rate of <see cref="Samples"/>.
    /// </summary>
    public const int SampleRate = 16_000;

    /// <summary>
    /// The duration of the audio.
    /// </summary>
    public TimeSpan Duration => TimeSpan.FromSeconds((double) Samples.Length / SampleRate);
}
