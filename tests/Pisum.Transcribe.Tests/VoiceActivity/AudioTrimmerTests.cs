using Pisum.Transcribe.Recording;
using Pisum.Transcribe.VoiceActivity;

namespace Pisum.Transcribe.Tests.VoiceActivity;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class AudioTrimmerTests
{
    private const int Pad = AudioTrimmer.DefaultPadSamples;

    [Fact]
    public void Trim_NoSegments_ReturnsNull()
    {
        // Arrange
        var samples = Ramp(Seconds(3));

        // Act
        var trimmed = AudioTrimmer.Trim(samples, []);

        // Assert
        trimmed.ShouldBeNull();
    }

    [Fact]
    public void Trim_TwoSecondsOfSpeechInSixSecondClip_KeepsSpeechPlus300MsOnEachSide()
    {
        // Arrange
        var samples = Ramp(Seconds(6));
        SpeechSegment[] segments = [new(Seconds(2), Seconds(4))];

        // Act
        var trimmed = AudioTrimmer.Trim(samples, segments);

        // Assert
        trimmed.ShouldNotBeNull();
        trimmed.Length.ShouldBe(Seconds(2.6));
        trimmed[0].ShouldBe(samples[Seconds(2) - Pad]);
        trimmed[^1].ShouldBe(samples[Seconds(4) + Pad - 1]);
    }

    [Fact]
    public void Trim_SpeechAtFirstSample_StartsAtFirstSample()
    {
        // Arrange
        var samples = Ramp(Seconds(3));
        SpeechSegment[] segments = [new(0, Seconds(1))];

        // Act
        var trimmed = AudioTrimmer.Trim(samples, segments);

        // Assert
        trimmed.ShouldNotBeNull();
        trimmed[0].ShouldBe(samples[0]);
        trimmed.Length.ShouldBe(Seconds(1) + Pad);
    }

    [Fact]
    public void Trim_SpeechToLastSample_EndsAtLastSample()
    {
        // Arrange
        var samples = Ramp(Seconds(3));
        SpeechSegment[] segments = [new(Seconds(2), samples.Length)];

        // Act
        var trimmed = AudioTrimmer.Trim(samples, segments);

        // Assert
        trimmed.ShouldNotBeNull();
        trimmed[^1].ShouldBe(samples[^1]);
        trimmed.Length.ShouldBe(Seconds(1) + Pad);
    }

    [Fact]
    public void Trim_TwoSegmentsWithPause_KeepsPause()
    {
        // Arrange
        var samples = Ramp(Seconds(8));
        SpeechSegment[] segments = [new(Seconds(1), Seconds(2.5)), new(Seconds(4), Seconds(6))];

        // Act
        var trimmed = AudioTrimmer.Trim(samples, segments);

        // Assert
        trimmed.ShouldBe(samples[(Seconds(1) - Pad)..(Seconds(6) + Pad)]);
    }

    [Fact]
    public void Trim_SpeechWithinPadOfBothEnds_ReturnsSameArray()
    {
        // Arrange
        var samples = Ramp(Seconds(2));
        SpeechSegment[] segments = [new(Seconds(0.1), Seconds(1.9))];

        // Act
        var trimmed = AudioTrimmer.Trim(samples, segments);

        // Assert
        trimmed.ShouldBeSameAs(samples);
    }

    private static int Seconds(double seconds)
    {
        return (int) Math.Round(seconds * AudioClip.SampleRate);
    }

    /// <summary>
    /// Samples that differ from each other, so a test can tell where a cut was made.
    /// </summary>
    private static float[] Ramp(int length)
    {
        return Enumerable.Range(0, length).Select(index => (float) index / length).ToArray();
    }
}
