using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;

namespace Pisum.Transcribe.VoiceActivity;

/// <summary>
/// Loads the voice activity detector and runs one inference in the background at startup, so the first dictation does
/// not pay the load. A failure is logged once and does not stop the application.
/// </summary>
internal sealed class VoiceActivityWarmupService : IHostedService
{
    private readonly IVoiceActivityDetector _detector;
    private readonly ILogger<VoiceActivityWarmupService> _logger;
    private Task _warmUp = Task.CompletedTask;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="detector">The voice activity detector.</param>
    /// <param name="logger">The logger.</param>
    public VoiceActivityWarmupService(IVoiceActivityDetector detector, ILogger<VoiceActivityWarmupService> logger)
    {
        _detector = detector;
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
