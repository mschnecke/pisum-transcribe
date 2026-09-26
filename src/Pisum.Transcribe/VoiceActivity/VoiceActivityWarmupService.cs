using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.VoiceActivity;

/// <summary>
/// Loads the voice activity detector and runs one inference in the background at startup, so the first dictation does
/// not pay the load. A failure is logged once and does not stop the application.
/// </summary>
internal sealed class VoiceActivityWarmupService : IHostedService
{
    /// <summary>
    /// The reason of the process activity that the warm-up runs in, as macOS lists it.
    /// </summary>
    public const string ActivityReason = "Warming up voice activity detection";

    private readonly IVoiceActivityDetector _detector;
    private readonly IProcessActivity _processActivity;
    private readonly ILogger<VoiceActivityWarmupService> _logger;
    private Task _warmUp = Task.CompletedTask;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="detector">The voice activity detector.</param>
    /// <param name="processActivity">Keeps macOS from throttling the warm-up through App Nap.</param>
    /// <param name="logger">The logger.</param>
    public VoiceActivityWarmupService(IVoiceActivityDetector detector,
                                      IProcessActivity processActivity,
                                      ILogger<VoiceActivityWarmupService> logger)
    {
        _detector = detector;
        _processActivity = processActivity;
        _logger = logger;
    }

    /// <summary>
    /// Starts the warm-up without waiting for it.
    /// </summary>
    /// <param name="cancellationToken">Not used; the warm-up cannot be cancelled.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _warmUp = Task.Run(WarmUp, CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Waits for a running warm-up, so the detector is not disposed while it loads.
    /// </summary>
    /// <param name="cancellationToken">Ends the wait when the shutdown timeout is reached.</param>
    /// <returns>A task that completes when the warm-up has ended or the wait was cancelled.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _warmUp.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private void WarmUp()
    {
        using var activity = _processActivity.Begin(ActivityReason);
        var started = Stopwatch.GetTimestamp();
        try
        {
            _detector.DetectSpeech(new float[SileroVadModel.WindowSize], CancellationToken.None);
            _logger.LogInformation("Voice activity detection is ready after {LoadMilliseconds:0} ms on ONNX Runtime {OnnxRuntimeVersion}",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds, OrtEnv.Instance().GetVersionString());
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception,
                "Voice activity detection is unavailable, so dictations are transcribed without trimming silence");
        }
    }
}
