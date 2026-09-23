using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.Recording;

namespace Pisum.Transcribe.Tests.Recording;

/// <summary>
/// Records the default microphone. Run explicitly on the target laptop; the audio stays in memory.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Hardware)]
public sealed class AudioRecorderHardwareTests : IAsyncDisposable
{
    private static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LatencyTarget = TimeSpan.FromMilliseconds(200);

    private readonly AudioRecorder _sut = new(new WasapiCaptureSessionFactory(), TimeProvider.System,
        NullLogger<AudioRecorder>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync()
    {
        return _sut.DisposeAsync();
    }

    [Fact(Explicit = true)]
    public async Task StopAsync_AfterTwoSeconds_ReturnsAbout32000SamplesInRange()
    {
        // Arrange
        await _sut.StartAsync(MaxDuration, Ct);
        await Task.Delay(TimeSpan.FromSeconds(2), Ct);

        // Act
        var clip = await _sut.StopAsync();

        // Assert
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{clip.Samples.Length} samples, peak {clip.Samples.Max(Math.Abs):G3}, non-zero {clip.Samples.Count(sample => sample != 0f)}");
        clip.Samples.Length.ShouldBeInRange(28_800, 35_200);
        clip.Samples.ShouldAllBe(sample => sample >= -1f && sample <= 1f);
    }

    [Fact(Explicit = true)]
    public async Task StartAsync_DefaultMicrophone_ReportsTimeToFirstAudio()
    {
        // Arrange
        var durations = new List<TimeSpan>();

        // Act
        for (var i = 0; i < 5; i++)
        {
            var started = Stopwatch.GetTimestamp();
            await _sut.StartAsync(MaxDuration, Ct);
            durations.Add(Stopwatch.GetElapsedTime(started));
            await _sut.AbortAsync();
        }

        // Assert
        var output = TestContext.Current.TestOutputHelper;
        foreach (var duration in durations)
        {
            output?.WriteLine($"StartAsync took {duration.TotalMilliseconds:0} ms" +
                              (duration > LatencyTarget ? $" (over the {LatencyTarget.TotalMilliseconds:0} ms target)" : ""));
        }

        durations.ShouldAllBe(duration => duration < AudioRecorder.StartTimeout);
    }

    [Fact(Explicit = true)]
    public async Task StartAndStop_FiftyCycles_KeepsHandleCountStable()
    {
        // Arrange: the Windows audio stack allocates a one-time step of about 130 handles in the first cycles.
        for (var i = 0; i < 100; i++)
        {
            await _sut.StartAsync(MaxDuration, Ct);
            await _sut.StopAsync();
        }

        var handlesBefore = HandleCount();

        // Act
        for (var i = 0; i < 50; i++)
        {
            await _sut.StartAsync(MaxDuration, Ct);
            await _sut.StopAsync();
        }

        // Assert
        var handlesAfter = HandleCount();
        TestContext.Current.TestOutputHelper?.WriteLine($"Handles before {handlesBefore}, after {handlesAfter}");
        handlesAfter.ShouldBeLessThanOrEqualTo(handlesBefore + 20);
    }

    [Fact(Explicit = true)]
    public async Task AbortAsync_Recording_ReturnsNothing()
    {
        // Arrange
        await _sut.StartAsync(MaxDuration, Ct);
        await Task.Delay(TimeSpan.FromMilliseconds(500), Ct);

        // Act
        await _sut.AbortAsync();

        // Assert
        _sut.IsRecording.ShouldBeFalse();
        await Should.ThrowAsync<InvalidOperationException>(() => _sut.StopAsync());
    }

    private static int HandleCount()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var process = Process.GetCurrentProcess();
        return process.HandleCount;
    }
}
