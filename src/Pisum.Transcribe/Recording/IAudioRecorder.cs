namespace Pisum.Transcribe.Recording;

/// <summary>
/// Records the microphone for dictation: the Windows default recording device, as <see cref="AudioClip"/>s. The
/// microphone is open only while a recording runs.
/// </summary>
/// <remarks>
/// <list type="table">
/// <listheader><term>State</term><description>Meaning</description></listheader>
/// <item><term>Idle</term><description>No recording. <see cref="StartAsync"/> opens the microphone.</description></item>
/// <item><term>Recording</term><description>Audio flows. <see cref="StopAsync"/> returns it, <see cref="AbortAsync"/> discards it.</description></item>
/// <item><term>Limit reached</term><description>The maximum duration was reached, the microphone is released and <see cref="MaxDurationReached"/> was raised. <see cref="StopAsync"/> returns the kept audio.</description></item>
/// <item><term>Failed</term><description>The microphone was lost, the audio discarded and <see cref="Failed"/> raised. <see cref="StopAsync"/> throws the error; <see cref="StartAsync"/> starts a new recording.</description></item>
/// </list>
/// Events are raised on thread-pool threads.
/// </remarks>
internal interface IAudioRecorder
{
    /// <summary>
    /// <see langword="true"/> while audio is being recorded, that is, after <see cref="StartAsync"/> has completed and
    /// before the recording was stopped, aborted, failed or reached its maximum duration.
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// Raised once when a recording reaches its maximum duration. The microphone is already released, and
    /// <see cref="StopAsync"/> returns the kept audio.
    /// </summary>
    event EventHandler? MaxDurationReached;

    /// <summary>
    /// Raised once when the microphone is lost during a recording. The audio is discarded, and the microphone is
    /// released.
    /// </summary>
    event EventHandler<RecordingFailedException>? Failed;

    /// <summary>
    /// Opens the microphone and completes when it delivers audio that is not silence: not marked as silence by Windows
    /// and not all zeros. Leading silence is discarded.
    /// </summary>
    /// <param name="maxDuration">The duration after which the recording stops by itself.</param>
    /// <param name="cancellationToken">A token to cancel the start. The microphone is released.</param>
    /// <returns>A task that completes when the recording runs.</returns>
    /// <exception cref="RecordingFailedException">The microphone could not be opened, or delivered no audio.</exception>
    /// <exception cref="InvalidOperationException">A recording is running or starting.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task StartAsync(TimeSpan maxDuration, CancellationToken cancellationToken);

    /// <summary>
    /// Stops the recording and releases the microphone.
    /// </summary>
    /// <returns>The audio recorded since the start.</returns>
    /// <exception cref="MicrophoneDisconnectedException">The microphone was lost during the recording.</exception>
    /// <exception cref="InvalidOperationException">No recording is running, or a start is pending.</exception>
    Task<AudioClip> StopAsync();

    /// <summary>
    /// Stops the recording, discards its audio and releases the microphone. Has no effect when idle.
    /// </summary>
    /// <returns>A task that completes when the microphone is released.</returns>
    /// <exception cref="InvalidOperationException">A start is pending.</exception>
    Task AbortAsync();
}
