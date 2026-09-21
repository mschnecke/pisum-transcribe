using Pisum.Transcribe.Recording;
using Pisum.Transcribe.VoiceActivity;

namespace Pisum.Transcribe.Tests.VoiceActivity;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class SileroVoiceActivityDetectorTests : IDisposable
{
    private const int ThreeSeconds = 3 * AudioClip.SampleRate;

    private readonly SileroVoiceActivityDetector _sut = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _sut.Dispose();
    }

    [Fact]
    public void DetectSpeech_ThreeSecondsOfZeros_FindsNoSpeech()
    {
        // Act
        var segments = _sut.DetectSpeech(new float[ThreeSeconds], Ct);

        // Assert
        segments.ShouldBeEmpty();
    }

    [Fact]
    public void DetectSpeech_ThreeSecondsOfLowWhiteNoise_FindsNoSpeech()
    {
        // Act
        var segments = _sut.DetectSpeech(Noise.Create(ThreeSeconds, 0.005f), Ct);

        // Assert
        segments.ShouldBeEmpty();
    }

    [Fact]
    public void DetectSpeech_EmptyClip_FindsNoSpeech()
    {
        // Act
        var segments = _sut.DetectSpeech([], Ct);

        // Assert
        segments.ShouldBeEmpty();
    }

    [Fact]
    public void DetectSpeech_SameClipTwice_ReturnsIdenticalSegments()
    {
        // Arrange
        var clip = Noise.Create(ThreeSeconds, 0.005f);
        var first = _sut.DetectSpeech(clip, Ct);

        // Act
        var second = _sut.DetectSpeech(clip, Ct);

        // Assert
        second.ShouldBe(first);
    }

    [Fact]
    public void DetectSpeech_ModelFileMissing_ThrowsOnEveryCall()
    {
        // Arrange
        using var root = new TempDirectory();
        using var sut = new SileroVoiceActivityDetector(() => new SileroVadModel(Path.Combine(root.Path, "missing.onnx")));
        var first = Should.Throw<Exception>(() => sut.DetectSpeech(new float[ThreeSeconds], Ct));

        // Act
        var second = Should.Throw<Exception>(() => sut.DetectSpeech(new float[ThreeSeconds], Ct));

        // Assert
        second.ShouldBeSameAs(first);
    }

    [Fact]
    public void DetectSpeech_TokenCancelled_ThrowsOperationCanceled()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act & Assert
        Should.Throw<OperationCanceledException>(() => _sut.DetectSpeech(new float[ThreeSeconds], cancellation.Token));
    }
}
