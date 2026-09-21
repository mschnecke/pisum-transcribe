using Pisum.Transcribe.VoiceActivity;

namespace Pisum.Transcribe.Tests.VoiceActivity;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class SileroSpeechDetectorTests
{
    private const int Window = SileroVadModel.WindowSize;

    [Fact]
    public void FindSegments_SpeechBetweenSilence_EndsWhereSilenceStarts()
    {
        // Arrange: 10 silent windows, 20 speech windows (640 ms), 10 silent windows.
        var probabilities = Probabilities((10, 0.1f), (20, 0.9f), (10, 0.1f));

        // Act
        var segments = SileroSpeechDetector.FindSegments(probabilities, probabilities.Length * Window);

        // Assert
        segments.ShouldBe([new SpeechSegment(10 * Window, 30 * Window)]);
    }

    [Fact]
    public void FindSegments_SpeechShorterThan250Ms_IsDropped()
    {
        // Arrange: 7 speech windows are 224 ms.
        var probabilities = Probabilities((10, 0.1f), (7, 0.9f), (10, 0.1f));

        // Act
        var segments = SileroSpeechDetector.FindSegments(probabilities, probabilities.Length * Window);

        // Assert
        segments.ShouldBeEmpty();
    }

    [Fact]
    public void FindSegments_SilenceShorterThan100Ms_DoesNotSplitSpeech()
    {
        // Arrange: 3 silent windows are 96 ms.
        var probabilities = Probabilities((5, 0.1f), (10, 0.9f), (3, 0.1f), (10, 0.9f), (10, 0.1f));

        // Act
        var segments = SileroSpeechDetector.FindSegments(probabilities, probabilities.Length * Window);

        // Assert
        segments.ShouldBe([new SpeechSegment(5 * Window, 28 * Window)]);
    }

    [Fact]
    public void FindSegments_ProbabilityBetweenThresholds_KeepsSpeechGoing()
    {
        // Arrange: 0.4 is below the threshold but above the negative threshold.
        var probabilities = Probabilities((5, 0.1f), (10, 0.9f), (10, 0.4f), (10, 0.1f));

        // Act
        var segments = SileroSpeechDetector.FindSegments(probabilities, probabilities.Length * Window);

        // Assert
        segments.ShouldBe([new SpeechSegment(5 * Window, 25 * Window)]);
    }

    [Fact]
    public void FindSegments_SpeechToTheEnd_EndsAtLastSample()
    {
        // Arrange: the last window holds only 100 samples of the recording.
        var probabilities = Probabilities((5, 0.1f), (20, 0.9f));
        var sampleCount = 24 * Window + 100;

        // Act
        var segments = SileroSpeechDetector.FindSegments(probabilities, sampleCount);

        // Assert
        segments.ShouldBe([new SpeechSegment(5 * Window, sampleCount)]);
    }

    private static float[] Probabilities(params (int Windows, float Probability)[] runs)
    {
        return runs.SelectMany(run => Enumerable.Repeat(run.Probability, run.Windows)).ToArray();
    }
}
