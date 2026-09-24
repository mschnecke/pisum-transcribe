using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Recording;

namespace Pisum.Transcribe.Tests.Recording;

/// <summary>
/// Records the default input device on macOS. Run explicitly on the dev Mac; the audio stays in memory. The test host
/// isn't an app bundle, so the microphone grant belongs to the terminal or Rider that runs it.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Hardware)]
public sealed class AudioRecorderHardwareTests : IAsyncDisposable
{
    private static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LatencyTarget = TimeSpan.FromMilliseconds(200);

    private readonly AudioRecorder _sut = new(CreateFactory(), TimeProvider.System, NullLogger<AudioRecorder>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync()
    {
        return _sut.DisposeAsync();
    }

    [Fact(Explicit = true)]
    public async Task StopAsync_AfterTwoSeconds_ReturnsAbout32000SamplesInRange()
    {
        // Arrange
        SkipWithoutMicrophone();
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
        SkipWithoutMicrophone();
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
    public async Task StartAndStop_FiftyCycles_KeepsFileDescriptorCountStable()
    {
        // Arrange: CoreAudio opens its connections to the audio server in the first cycles.
        SkipWithoutMicrophone();
        for (var i = 0; i < 20; i++)
        {
            await _sut.StartAsync(MaxDuration, Ct);
            await _sut.StopAsync();
        }

        var descriptorsBefore = FileDescriptorCount();

        // Act
        for (var i = 0; i < 50; i++)
        {
            await _sut.StartAsync(MaxDuration, Ct);
            await _sut.StopAsync();
        }

        // Assert
        var descriptorsAfter = FileDescriptorCount();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"File descriptors before {descriptorsBefore}, after {descriptorsAfter}");
        descriptorsAfter.ShouldBeLessThanOrEqualTo(descriptorsBefore + 5);
    }

    [Fact(Explicit = true)]
    public async Task AbortAsync_Recording_ReturnsNothing()
    {
        // Arrange
        SkipWithoutMicrophone();
        await _sut.StartAsync(MaxDuration, Ct);
        await Task.Delay(TimeSpan.FromMilliseconds(500), Ct);

        // Act
        await _sut.AbortAsync();

        // Assert
        _sut.IsRecording.ShouldBeFalse();
        await Should.ThrowAsync<InvalidOperationException>(() => _sut.StopAsync());
    }

    [Fact(Explicit = true)]
    public async Task StopAsync_InputDeviceSwitchedDuringRecording_ContinuesOnTheNewDevice()
    {
        // Arrange
        SkipWithoutMicrophone();
        var original = CoreAudio.GetDefaultInputDevice();
        var other = CoreAudioTestDevices.InputDevices().FirstOrDefault(device => device != original);
        Assert.SkipWhen(other == CoreAudio.UnknownObject, "Only one input device is present.");
        await _sut.StartAsync(MaxDuration, Ct);

        try
        {
            // Act: a wireless device, such as an iPhone's microphone, needs several seconds to deliver audio.
            await Task.Delay(TimeSpan.FromSeconds(1), Ct);
            CoreAudioTestDevices.SetDefaultInputDevice(other);
            await Task.Delay(TimeSpan.FromSeconds(8), Ct);
            var clip = await _sut.StopAsync();

            // Assert: about 1 s from the first device, and at least 1 s from the new one.
            TestContext.Current.TestOutputHelper?.WriteLine($"{clip.Samples.Length} samples");
            clip.Samples.Length.ShouldBeGreaterThan(2 * AudioClip.SampleRate);
        }
        finally
        {
            CoreAudioTestDevices.SetDefaultInputDevice(original);
        }
    }

    [Fact(Explicit = true)]
    public async Task StartAsync_MutedInputDevice_ThrowsMicrophoneMuted()
    {
        // Arrange
        SkipWithoutMicrophone();
        var device = CoreAudio.GetDefaultInputDevice();
        Assert.SkipUnless(CoreAudioTestDevices.CanSetInputMute(device),
            "The default input device has no mute state that can be set.");
        var wasMuted = CoreAudio.IsInputMuted(device);
        CoreAudioTestDevices.SetInputMute(device, true);

        try
        {
            // Act
            var start = _sut.StartAsync(MaxDuration, Ct);

            // Assert
            await Should.ThrowAsync<MicrophoneMutedException>(start);
            _sut.IsRecording.ShouldBeFalse();
        }
        finally
        {
            CoreAudioTestDevices.SetInputMute(device, wasMuted);
        }
    }

    private static AudioQueueCaptureSessionFactory CreateFactory()
    {
        return new AudioQueueCaptureSessionFactory(new MacNativeLibrary(NullLogger<MacNativeLibrary>.Instance),
            new InlineUiDispatcher());
    }

    private static void SkipWithoutMicrophone()
    {
        Assert.SkipWhen(PisumMac.MicrophoneStatus() != 3,
            "The microphone isn't allowed for the terminal or IDE that runs the tests.");
        Assert.SkipWhen(CoreAudio.GetDefaultInputDevice() == CoreAudio.UnknownObject, "No input device is present.");
    }

    private static int FileDescriptorCount()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return Directory.GetFileSystemEntries("/dev/fd").Length;
    }
}
