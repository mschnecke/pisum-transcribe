namespace Pisum.Transcribe.Transcription;

/// <summary>
/// A model loaded into the native speech engine, with one session. Not thread-safe: at most one call may run at a time,
/// and <see cref="IDisposable.Dispose"/> must not run while <see cref="Run"/> does.
/// </summary>
internal interface INativeSpeechEngine : IDisposable
{
    /// <summary>
    /// The longest audio input the session accepts, as the native engine reports it. Zero or less if unknown.
    /// </summary>
    TimeSpan MaxAudio { get; }

    /// <summary>
    /// Runs the model on audio. Blocks until the run has finished.
    /// </summary>
    /// <param name="samples">16 kHz mono 32-bit float samples in the range [-1, 1].</param>
    /// <param name="task">Whether speech is translated or transcribed.</param>
    /// <param name="sourceLanguage">The spoken language as an ISO 639-1 code.</param>
    /// <param name="targetLanguage">The output language of a translation. Ignored for a transcription.</param>
    /// <param name="cancellationToken">A token that aborts the native run.</param>
    /// <returns>The text, which is partial if the run stopped at the model's output limit.</returns>
    /// <exception cref="NativeEngineException">The run failed.</exception>
    /// <exception cref="OperationCanceledException">The run was aborted.</exception>
    NativeRunOutput Run(float[] samples,
                        TranscriptionTask task,
                        string sourceLanguage,
                        string targetLanguage,
                        CancellationToken cancellationToken);
}
