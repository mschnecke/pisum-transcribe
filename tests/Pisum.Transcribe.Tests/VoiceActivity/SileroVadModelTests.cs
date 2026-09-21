using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Pisum.Transcribe.VoiceActivity;

namespace Pisum.Transcribe.Tests.VoiceActivity;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class SileroVadModelTests
{
    [Fact]
    public void BundledModelPath_File_IsSileroVad622()
    {
        // Act
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(SileroVadModel.BundledModelPath)));

        // Assert
        hash.ShouldBe("1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3");
    }

    [Fact]
    public void OrtEnv_LoadedRuntime_IsPackagedVersionNotWindowsMl()
    {
        // Arrange
        using var model = new SileroVadModel(SileroVadModel.BundledModelPath);

        // Act
        var version = OrtEnv.Instance().GetVersionString();

        // Assert
        version.ShouldBe("1.30.0");
    }

    [Fact]
    public void Process_SameWindowsAfterReset_ReturnsIdenticalProbabilities()
    {
        // Arrange
        using var model = new SileroVadModel(SileroVadModel.BundledModelPath);
        var windows = Noise.Windows(10, 0.1f);
        var first = windows.Select(window => model.Process(window)).ToList();
        var carriedOver = model.Process(windows[0]);

        // Act
        model.Reset();
        var second = windows.Select(window => model.Process(window)).ToList();

        // Assert
        second.ShouldBe(first);
        first.ShouldAllBe(probability => probability >= 0 && probability <= 1);

        // Shows that the state matters, so the reset is what makes the runs equal.
        carriedOver.ShouldNotBe(first[0]);
    }

    [Fact]
    public void Process_ShortLastWindow_PadsWithZeros()
    {
        // Arrange
        using var model = new SileroVadModel(SileroVadModel.BundledModelPath);
        var window = Noise.Windows(1, 0.1f)[0];
        var padded = window[..100].Concat(new float[SileroVadModel.WindowSize - 100]).ToArray();
        var expected = model.Process(padded);
        model.Reset();

        // Act
        var probability = model.Process(window.AsSpan(0, 100));

        // Assert
        probability.ShouldBe(expected);
    }
}
