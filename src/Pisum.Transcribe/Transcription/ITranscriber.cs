using Pisum.Transcribe.SpeechModels;

namespace Pisum.Transcribe.Transcription;

/// <summary>
/// Turns 16 kHz mono audio into text with a speech model that stays loaded. Requests run one at a time, in arrival order.
/// </summary>
internal interface ITranscriber
{
    /// <summary>
    /// The current status.
    /// </summary>
    TranscriberStatus Status { get; }

    /// <summary>
    /// The backend in use, <c>Vulkan</c>, <c>Metal</c> or <c>CPU</c>, while <see cref="Status"/> is
    /// <see cref="TranscriberStatus.Ready"/>; otherwise <see langword="null"/>.
    /// </summary>
    string? ActiveBackend { get; }

    /// <summary>
    /// The longest audio input the loaded model accepts. 400 s while no model is loaded.
    /// </summary>
    TimeSpan MaxInputDuration { get; }

    /// <summary>
    /// A user-facing explanation of the failure while <see cref="Status"/> is <see cref="TranscriberStatus.Failed"/>;
    /// otherwise <see langword="null"/>.
    /// </summary>
    string? FailureMessage { get; }

    /// <summary>
    /// Raised after <see cref="Status"/> changed, on the thread that changed it, so subscribers that touch WPF or the
    /// tray marshal to the dispatcher.
    /// </summary>
    event EventHandler<TranscriberStatus>? StatusChanged;

    /// <summary>
    /// Loads a model in the background and warms it up, in any status. The status becomes
    /// <see cref="TranscriberStatus.Loading"/> before this method returns, then <see cref="TranscriberStatus.Ready"/> or
    /// <see cref="TranscriberStatus.Failed"/>. Requests submitted before this call complete on the previous model, which
    /// is then released before the new one loads. The newest call wins: the status stays
    /// <see cref="TranscriberStatus.Loading"/> until the model and backend of the most recent call are loaded or have
    /// failed.
    /// </summary>
    /// <param name="model">The installed catalog model.</param>
    /// <param name="backend">The backend preference.</param>
    /// <param name="cancellationToken">A token to stop waiting. It does not cancel the load.</param>
    /// <returns>
    /// A task that completes when this load has finished, whether it succeeded or failed, or when a newer call replaced
    /// it.
    /// </returns>
    Task LoadAsync(SpeechModel model, BackendPreference backend, CancellationToken cancellationToken);

    /// <summary>
    /// Transcribes or translates audio. Invalid requests are rejected before they are queued.
    /// </summary>
    /// <param name="samples">16 kHz mono 32-bit float samples in the range [-1, 1].</param>
    /// <param name="options">The task and languages.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The result. Empty audio gives an empty text without running the model.</returns>
    /// <exception cref="TranscriberNotReadyException">The status is not <see cref="TranscriberStatus.Ready"/>.</exception>
    /// <exception cref="LanguageNotSupportedException">The loaded model does not support the languages.</exception>
    /// <exception cref="AudioTooLongException">The audio is longer than <see cref="MaxInputDuration"/>.</exception>
    /// <exception cref="TranscriptionFailedException">The native engine failed.</exception>
    /// <exception cref="OperationCanceledException">The request was cancelled, or the application is stopping.</exception>
    Task<TranscriptionResult> TranscribeAsync(float[] samples,
                                              TranscriptionOptions options,
                                              CancellationToken cancellationToken);
}
