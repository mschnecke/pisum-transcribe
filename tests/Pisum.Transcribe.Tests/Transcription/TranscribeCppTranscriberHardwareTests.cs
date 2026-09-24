using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Transcription;

namespace Pisum.Transcribe.Tests.Transcription;

/// <summary>
/// Runs the real engine on the installed catalog models, most tests on the installed default model with the <c>auto</c>
/// backend. Needs an installed catalog model and, for the clip tests, the audio clips from
/// <see cref="HardwareTestAssets"/>.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Hardware)]
public sealed class TranscribeCppTranscriberHardwareTests : IAsyncDisposable
{
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromMinutes(2);

    private readonly TranscribeCppTranscriber _sut = new(new TranscribeCppEngineFactory(),
        HardwareTestAssets.ModelStore, NullLogger<TranscribeCppTranscriber>.Instance);

    public async ValueTask DisposeAsync()
    {
        await _sut.StopAsync(CancellationToken.None);
    }

    [Fact(Explicit = true)]
    public async Task TranscribeAsync_GermanClipTranslatedToEnglish_ReturnsText()
    {
        // Arrange
        var samples = HardwareTestAssets.ReadAudioOrSkip(HardwareTestAssets.GermanAudioVariable);
        await LoadAsync();

        // Act
        var result = await _sut.TranscribeAsync(samples,
            new TranscriptionOptions(TranscriptionTask.Translate, "de", "en"), TestContext.Current.CancellationToken);

        // Assert
        result.Text.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact(Explicit = true)]
    public async Task TranscribeAsync_GermanClipTranscribedWithEnglishTarget_ReturnsTextThatIsNotTheTranslation()
    {
        // Arrange
        var samples = HardwareTestAssets.ReadAudioOrSkip(HardwareTestAssets.GermanAudioVariable);
        await LoadAsync();
        var translation = await _sut.TranscribeAsync(samples,
            new TranscriptionOptions(TranscriptionTask.Translate, "de", "en"), TestContext.Current.CancellationToken);

        // Act
        var transcription = await _sut.TranscribeAsync(samples,
            new TranscriptionOptions(TranscriptionTask.Transcribe, "de", "en"), TestContext.Current.CancellationToken);

        // Assert
        transcription.Text.ShouldNotBeNullOrWhiteSpace();
        transcription.Text.ShouldNotBe(translation.Text);
    }

    [Fact(Explicit = true)]
    public async Task TranscribeAsync_EnglishClipTranslatedToGerman_ReturnsTextThatIsNotTheTranscription()
    {
        // Arrange
        var samples = HardwareTestAssets.ReadAudioOrSkip(HardwareTestAssets.EnglishAudioVariable);
        await LoadAsync();
        var transcription = await _sut.TranscribeAsync(samples,
            new TranscriptionOptions(TranscriptionTask.Transcribe, "en", "de"), TestContext.Current.CancellationToken);

        // Act
        var translation = await _sut.TranscribeAsync(samples,
            new TranscriptionOptions(TranscriptionTask.Translate, "en", "de"), TestContext.Current.CancellationToken);

        // Assert
        translation.Text.ShouldNotBeNullOrWhiteSpace();
        translation.Text.ShouldNotBe(transcription.Text);
    }

    [Fact(Explicit = true)]
    public async Task LoadAsync_ForcedGpu_IsReadyOnPlatformGpuBackend()
    {
        // Arrange
        var model = HardwareTestAssets.InstalledModelsOrSkip()[0];
        Assert.SkipUnless(new TranscribeCppEngineFactory().IsGpuAvailable(),
            $"No {TranscribeCppEngineFactory.GpuBackendName} device is available.");

        // Act
        await LoadAsync(model, BackendPreference.Gpu);

        // Assert
        _sut.ActiveBackend.ShouldBe(TranscribeCppEngineFactory.GpuBackendName);
    }

    [Fact(Explicit = true)]
    public async Task TranscribeAsync_AudioOfMaxInputDuration_ReturnsResult()
    {
        // Arrange
        var models = HardwareTestAssets.InstalledModelsOrSkip();
        var options = new TranscriptionOptions(TranscriptionTask.Transcribe, "en", "en");
        var runs = new List<(string ModelId, TimeSpan MaxInputDuration, TimeSpan AudioDuration)>();

        // Act
        foreach (var model in models)
        {
            // The length check does not depend on the backend, and the CPU backend cannot run out of GPU memory.
            await LoadAsync(model, BackendPreference.Cpu);
            var maxInputDuration = _sut.MaxInputDuration;

            // As many samples as the recorder keeps when a dictation reaches the maximum length, of silence as in
            // the CPU warm-up.
            var samples = new float[SampleAccumulator.MaxCountFor(maxInputDuration)];
            var started = Stopwatch.GetTimestamp();
            var result = await _sut.TranscribeAsync(samples, options, TestContext.Current.CancellationToken);
            runs.Add((model.Id, maxInputDuration, result.AudioDuration));
            TestContext.Current.SendDiagnosticMessage(FormattableString.Invariant(
                $"{model.Id}: maximum input {maxInputDuration.TotalSeconds:0.000} s, transcribed in {Stopwatch.GetElapsedTime(started).TotalSeconds:0.00} s"));
        }

        // Assert
        runs.ShouldAllBe(run => run.AudioDuration == run.MaxInputDuration);
    }

    private Task LoadAsync()
    {
        return LoadAsync(HardwareTestAssets.InstalledModelsOrSkip()[0], BackendPreference.Auto);
    }

    private async Task LoadAsync(SpeechModel model, BackendPreference backend)
    {
        await _sut.LoadAsync(model, backend, TestContext.Current.CancellationToken)
            .WaitAsync(LoadTimeout, TestContext.Current.CancellationToken);
        _sut.Status.ShouldBe(TranscriberStatus.Ready, _sut.FailureMessage);
        TestContext.Current.SendDiagnosticMessage($"{model.Id} is ready on {_sut.ActiveBackend}");
    }
}
