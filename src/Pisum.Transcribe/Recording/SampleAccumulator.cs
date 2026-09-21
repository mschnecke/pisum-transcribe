using System.Buffers;

namespace Pisum.Transcribe.Recording;

/// <summary>
/// Collects the samples of one recording up to a maximum count. Not thread-safe.
/// </summary>
internal sealed class SampleAccumulator
{
    // Most dictations are shorter, so longer ones grow the buffer instead of every recording reserving the maximum.
    private const int InitialCapacity = 30 * AudioClip.SampleRate;

    private readonly ArrayBufferWriter<float> _samples;
    private readonly int _maxCount;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="maxCount">The number of samples after which further samples are dropped. Greater than zero.</param>
    public SampleAccumulator(int maxCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        _maxCount = maxCount;
        _samples = new ArrayBufferWriter<float>(Math.Min(maxCount, InitialCapacity));
    }

    /// <summary>
    /// The number of samples collected.
    /// </summary>
    public int Count => _samples.WrittenCount;

    /// <summary>
    /// <see langword="true"/> once the maximum count is reached.
    /// </summary>
    public bool IsFull => _samples.WrittenCount == _maxCount;

    /// <summary>
    /// Returns the maximum sample count for a duration.
    /// </summary>
    /// <param name="duration">The duration. Greater than zero.</param>
    /// <returns>The number of samples in <paramref name="duration"/> at <see cref="AudioClip.SampleRate"/>.</returns>
    public static int MaxCountFor(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        return checked((int) (duration.Ticks * AudioClip.SampleRate / TimeSpan.TicksPerSecond));
    }

    /// <summary>
    /// Appends samples clamped to [-1, 1], up to the maximum count. Samples beyond it are dropped.
    /// </summary>
    /// <param name="samples">The samples.</param>
    /// <returns><see langword="true"/> if the maximum count is reached.</returns>
    public bool Append(ReadOnlySpan<float> samples)
    {
        var count = Math.Min(samples.Length, _maxCount - _samples.WrittenCount);
        var destination = _samples.GetSpan(count);

        // Windows' float conversion does not clamp, so a loud peak can exceed full scale.
        for (var i = 0; i < count; i++)
        {
            destination[i] = Math.Clamp(samples[i], -1f, 1f);
        }

        _samples.Advance(count);
        return IsFull;
    }

    /// <summary>
    /// Copies the collected samples.
    /// </summary>
    /// <returns>A new array with the samples.</returns>
    public float[] ToArray()
    {
        return _samples.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Overwrites and forgets the collected samples.
    /// </summary>
    public void Clear()
    {
        _samples.Clear();
    }
}
