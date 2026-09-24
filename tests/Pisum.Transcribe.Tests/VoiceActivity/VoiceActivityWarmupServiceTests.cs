using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Tests.Hosting;
using Pisum.Transcribe.VoiceActivity;

namespace Pisum.Transcribe.Tests.VoiceActivity;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class VoiceActivityWarmupServiceTests
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);

    private readonly CapturingLogger<VoiceActivityWarmupService> _logger = new();
    private readonly RecordingProcessActivity _processActivity = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DetectSpeech_CalledWhileWarmupLoads_WaitsForLoadAndMatchesLaterCall()
    {
        // Arrange
        using var loadGate = new ManualResetEventSlim();
        using var detector = new SileroVoiceActivityDetector(() =>
        {
            loadGate.Wait(SignalTimeout);
            return new SileroVadModel(SileroVadModel.BundledModelPath);
        });
        var sut = new VoiceActivityWarmupService(detector, _processActivity, _logger);
        var clip = Noise.Create(3 * AudioClip.SampleRate, 0.1f);
        var start = sut.StartAsync(Ct);

        // Act
        var duringLoad = Task.Run(() => detector.DetectSpeech(clip, Ct), Ct);
        await Task.Delay(100, Ct);
        var completedBeforeLoad = duringLoad.IsCompleted;
        loadGate.Set();
        var segmentsDuringLoad = await duringLoad.WaitAsync(SignalTimeout, Ct);
        await sut.StopAsync(Ct).WaitAsync(SignalTimeout, Ct);
        var segmentsAfterLoad = detector.DetectSpeech(clip, Ct);

        // Assert
        start.IsCompletedSuccessfully.ShouldBeTrue();
        completedBeforeLoad.ShouldBeFalse();
        segmentsDuringLoad.ShouldBe(segmentsAfterLoad);
        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldContain("ONNX Runtime 1.30.0");
    }

    [Fact]
    public async Task StartAsync_ModelFileMissing_LogsOneWarningAndEveryDetectSpeechThrows()
    {
        // Arrange
        using var root = new TempDirectory();
        using var detector =
            new SileroVoiceActivityDetector(() => new SileroVadModel(Path.Combine(root.Path, "missing.onnx")));
        var sut = new VoiceActivityWarmupService(detector, _processActivity, _logger);

        // Act
        await sut.StartAsync(Ct);
        await sut.StopAsync(Ct).WaitAsync(SignalTimeout, Ct);

        // Assert
        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldNotBeNull();
        Should.Throw<Exception>(() => detector.DetectSpeech(new float[AudioClip.SampleRate], Ct));
        Should.Throw<Exception>(() => detector.DetectSpeech(new float[AudioClip.SampleRate], Ct));
    }

    [Fact]
    public async Task StartAsync_UntilWarmupEnds_RunsInOneActivity()
    {
        // Arrange
        using var loadGate = new ManualResetEventSlim();
        using var detector = new SileroVoiceActivityDetector(() =>
        {
            loadGate.Wait(SignalTimeout);
            return new SileroVadModel(SileroVadModel.BundledModelPath);
        });
        var sut = new VoiceActivityWarmupService(detector, _processActivity, _logger);

        // Act
        await sut.StartAsync(Ct);
        await WaitUntilAsync(() => _processActivity.Running == 1);
        loadGate.Set();
        await sut.StopAsync(Ct).WaitAsync(SignalTimeout, Ct);

        // Assert
        _processActivity.Begun.ShouldBe([VoiceActivityWarmupService.ActivityReason]);
        _processActivity.Running.ShouldBe(0);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var started = DateTime.UtcNow;
        while (!condition())
        {
            (DateTime.UtcNow - started).ShouldBeLessThan(SignalTimeout);
            await Task.Delay(5, Ct);
        }
    }
}
