using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Transcription;

namespace Pisum.Transcribe.Tests.Transcription;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class TranscribeCppTranscriberLoggingTests : IAsyncDisposable
{
    private const string Gpu = TranscribeCppEngineFactory.GpuBackendName;
    private const string Sentence = "The quarterly numbers stay confidential";

    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);

    private readonly FakeNativeSpeechEngineFactory _engineFactory = new();
    private readonly CapturingLogger<TranscribeCppTranscriber> _logger = new();
    private readonly TranscribeCppTranscriber _sut;

    public TranscribeCppTranscriberLoggingTests()
    {
        var modelStore = A.Fake<IModelStore>();
        A.CallTo(() => modelStore.GetModelPath(A<SpeechModel>._)).Returns(@"C:\models\model.gguf");
        _sut = new TranscribeCppTranscriber(_engineFactory, modelStore, _logger);
    }

    public async ValueTask DisposeAsync()
    {
        await _sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TranscribeAsync_CompletedAndTruncatedTranscriptions_LogDurationsButNoText()
    {
        // Arrange
        _engineFactory.OnRun = run => run.IsWarmUp
            ? new NativeRunOutput(string.Empty, false)
            : new NativeRunOutput(Sentence, run.Samples.Length > 16_000);
        await _sut.LoadAsync(ModelCatalog.Resolve(null), BackendPreference.Auto, TestContext.Current.CancellationToken)
            .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        var options = new TranscriptionOptions(TranscriptionTask.Translate, "de", "en");

        // Act
        var completed = await _sut.TranscribeAsync(Audio(1), options, TestContext.Current.CancellationToken);
        var truncated = await _sut.TranscribeAsync(Audio(2), options, TestContext.Current.CancellationToken);

        // Assert
        completed.Text.ShouldBe(Sentence);
        truncated.Text.ShouldBe(Sentence);
        var entries = _logger.Entries;
        entries.ShouldNotBeEmpty();
        foreach (var entry in entries)
        {
            entry.Message.ShouldNotContain(Sentence);
            entry.Properties.Select(property => Convert.ToString(property.Value)).ShouldNotContain(Sentence);
            entry.Exception.ShouldBeNull();
        }

        entries.Count(entry => entry.Properties.Any(property => property.Key == "AudioDuration")
                               && entry.Properties.Any(property => property.Key == "ProcessingTime"))
            .ShouldBe(2);
        entries.ShouldContain(entry => entry.Message.Contains("output limit"));
    }

    [Fact]
    public async Task TranscribeAsync_FailedTranscription_LogsStatusButNoText()
    {
        // Arrange
        _engineFactory.OnRun = run => run.IsWarmUp
            ? new NativeRunOutput(Sentence, false)
            : throw new NativeEngineException(NativeStatus.ErrInvalidArg);
        await _sut.LoadAsync(ModelCatalog.Resolve(null), BackendPreference.Cpu, TestContext.Current.CancellationToken)
            .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Act
        var transcription = _sut.TranscribeAsync(Audio(1), new TranscriptionOptions(TranscriptionTask.Translate, "de",
            "en"), TestContext.Current.CancellationToken);

        // Assert
        await Should.ThrowAsync<TranscriptionFailedException>(transcription);
        var entries = _logger.Entries;
        entries.ShouldContain(entry => entry.Message.Contains(nameof(NativeStatus.ErrInvalidArg)));
        foreach (var entry in entries)
        {
            entry.Message.ShouldNotContain(Sentence);
            (entry.Exception?.ToString() ?? "").ShouldNotContain(Sentence);
        }
    }

    [Fact]
    public async Task TranscribeAsync_OutOfMemoryReturnAndStayOnCpu_LogDurationsButNoText()
    {
        // Arrange
        _engineFactory.OnRun = run =>
            run is {IsWarmUp: false, Backend: NativeBackend.Gpu}
            && run.Samples.Length is 300 * TranscribeCppTranscriber.SampleRate or 90 * TranscribeCppTranscriber.SampleRate
                ? throw new NativeEngineException(NativeStatus.ErrOom)
                : new NativeRunOutput(Sentence, false);
        await _sut.LoadAsync(ModelCatalog.Resolve(null), BackendPreference.Auto, TestContext.Current.CancellationToken)
            .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        var options = new TranscriptionOptions(TranscriptionTask.Translate, "de", "en");
        await _sut.TranscribeAsync(Audio(120), options, TestContext.Current.CancellationToken);

        // Act
        await _sut.TranscribeAsync(Audio(300), options, TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => _sut.ActiveBackend == Gpu);
        await _sut.TranscribeAsync(Audio(90), options, TestContext.Current.CancellationToken);

        // Runs after the worker has decided to stay on the CPU backend.
        await _sut.TranscribeAsync(Audio(1), options, TestContext.Current.CancellationToken);

        // Assert
        var entries = _logger.Entries.ToList();
        var returning = entries.FindIndex(entry => entry.Level == LogLevel.Warning
                                                   && HasProperty(entry, "AudioDuration", TimeSpan.FromSeconds(300)));
        var staying = entries.FindIndex(entry => entry.Level == LogLevel.Information
                                                 && HasProperty(entry, "AudioDuration", TimeSpan.FromSeconds(90))
                                                 && HasProperty(entry, "LongestGpuRun",
                                                     TimeSpan.FromSeconds(120)));
        returning.ShouldBeGreaterThanOrEqualTo(0);
        entries[returning].Message.ShouldContain(Gpu);
        staying.ShouldBeGreaterThan(returning);
        entries[staying].Message.ShouldContain("CPU");
        foreach (var entry in entries)
        {
            entry.Message.ShouldNotContain(Sentence);
            entry.Properties.Select(property => Convert.ToString(property.Value)).ShouldNotContain(Sentence);
            (entry.Exception?.ToString() ?? "").ShouldNotContain(Sentence);
        }
    }

    private static float[] Audio(int seconds)
    {
        var samples = new float[seconds * TranscribeCppTranscriber.SampleRate];
        Array.Fill(samples, 0.1f);
        return samples;
    }

    private static bool HasProperty(LogEntry entry, string key, object value)
    {
        return entry.Properties.Any(property => property.Key == key && Equals(property.Value, value));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            stopwatch.Elapsed.ShouldBeLessThan(SignalTimeout);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
