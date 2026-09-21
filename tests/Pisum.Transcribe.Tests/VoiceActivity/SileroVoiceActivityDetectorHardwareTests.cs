using System.Diagnostics;
using System.Globalization;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Tests.Transcription;
using Pisum.Transcribe.VoiceActivity;

namespace Pisum.Transcribe.Tests.VoiceActivity;

/// <summary>
/// Runs the detector on the German clip from <see cref="HardwareTestAssets"/>. The timings go to the diagnostic
/// messages, which <c>dotnet test</c> does not print. Run the test executable instead:
/// <c>Pisum.Transcribe.Tests.exe -explicit only -class "*.SileroVoiceActivityDetectorHardwareTests" -diagnostics</c>.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Hardware)]
public sealed class SileroVoiceActivityDetectorHardwareTests : IDisposable
{
    private const int TwoSeconds = 2 * AudioClip.SampleRate;

    // Two model windows.
    private const int Tolerance = 2 * SileroVadModel.WindowSize;
    private const int BenchmarkRuns = 5;
    private static readonly TimeSpan MaxDetectionTime = TimeSpan.FromMilliseconds(300);

    private readonly SileroVoiceActivityDetector _sut = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _sut.Dispose();
    }

    [Fact(Explicit = true)]
    public void DetectSpeech_GermanClipPaddedWithTwoSecondsOfSilence_TrimsAddedSilenceCompletely()
    {
        // Arrange
        var original = HardwareTestAssets.ReadAudioOrSkip(HardwareTestAssets.GermanAudioVariable);
        var padded = new float[TwoSeconds + original.Length + TwoSeconds];
        original.CopyTo(padded, TwoSeconds);
        var originalSegments = _sut.DetectSpeech(original, Ct);

        // Act
        var segments = _sut.DetectSpeech(padded, Ct);
        var trimmed = AudioTrimmer.Trim(padded, segments);

        // Assert
        segments.ShouldNotBeEmpty();
        originalSegments.ShouldNotBeEmpty();
        trimmed.ShouldNotBeNull();

        // The trim of the original clip is clamped where its speech is closer to an end than the padding, so the
        // expected length is its speech plus the full padding.
        var expected = originalSegments[^1].EndSample - originalSegments[0].StartSample +
                       2 * AudioTrimmer.DefaultPadSamples;
        TestContext.Current.SendDiagnosticMessage(FormattableString.Invariant(
            $"Original {Ms(original.Length)} ms, speech {Ms(originalSegments[0].StartSample)}–{Ms(originalSegments[^1].EndSample)} ms in {originalSegments.Count} segments; padded {Ms(padded.Length)} ms trimmed to {Ms(trimmed.Length)} ms, expected {Ms(expected)} ms"));
        trimmed.Length.ShouldBeInRange(expected - Tolerance, expected + Tolerance);
    }

    [Fact(Explicit = true)]
    public void DetectSpeech_GermanClipAttenuatedBy20Db_FindsSpeech()
    {
        // Arrange
        var quiet = HardwareTestAssets.ReadAudioOrSkip(HardwareTestAssets.GermanAudioVariable)
            .Select(sample => sample * 0.1f)
            .ToArray();

        // Act
        var segments = _sut.DetectSpeech(quiet, Ct);

        // Assert
        segments.ShouldNotBeEmpty();
    }

    [Fact(Explicit = true)]
    public void DetectSpeech_ThirtySecondsOfSpeech_TakesUnder300MsMedian()
    {
        // Arrange
        var clip = HardwareTestAssets.ReadAudioOrSkip(HardwareTestAssets.GermanAudioVariable);
        var samples = new float[30 * AudioClip.SampleRate];
        for (var offset = 0; offset < samples.Length; offset += clip.Length)
        {
            clip.AsSpan(0, Math.Min(clip.Length, samples.Length - offset)).CopyTo(samples.AsSpan(offset));
        }

        // Loads the model, as the warm-up does at startup.
        _sut.DetectSpeech(new float[SileroVadModel.WindowSize], Ct);

        // Act
        var runs = new List<TimeSpan>();
        for (var run = 0; run < BenchmarkRuns; run++)
        {
            var started = Stopwatch.GetTimestamp();
            _sut.DetectSpeech(samples, Ct);
            runs.Add(Stopwatch.GetElapsedTime(started));
        }

        // Assert
        var median = runs.Order().ElementAt(BenchmarkRuns / 2);
        TestContext.Current.SendDiagnosticMessage(string.Create(CultureInfo.InvariantCulture,
            $"30 s of audio: median {median.TotalMilliseconds:0} ms, runs {string.Join(", ", runs.Select(time => time.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)))} ms"));
        median.ShouldBeLessThan(MaxDetectionTime);
    }

    private static int Ms(int samples)
    {
        return samples * 1000 / AudioClip.SampleRate;
    }
}
