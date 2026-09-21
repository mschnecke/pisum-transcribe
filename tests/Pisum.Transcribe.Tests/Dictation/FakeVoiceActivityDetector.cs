using Pisum.Transcribe.VoiceActivity;

namespace Pisum.Transcribe.Tests.Dictation;

/// <summary>
/// Answers speech detection from a delegate and counts the calls, from any thread. FakeItEasy cannot match the
/// <see cref="ReadOnlySpan{T}"/> parameter.
/// </summary>
internal sealed class FakeVoiceActivityDetector : IVoiceActivityDetector
{
    private int _calls;

    /// <summary>
    /// Gets the segments for a recording of the given length. By default the whole recording is speech.
    /// </summary>
    public Func<int, CancellationToken, IReadOnlyList<SpeechSegment>> Detect { get; set; } =
        (length, _) => [new SpeechSegment(0, length)];

    public int Calls => Volatile.Read(ref _calls);

    public IReadOnlyList<SpeechSegment> DetectSpeech(ReadOnlySpan<float> samples, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Detect(samples.Length, cancellationToken);
    }
}
