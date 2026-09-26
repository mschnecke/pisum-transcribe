namespace Pisum.Transcribe.Dictation;

/// <summary>
/// Whether a dictation is in progress, from the press that passed the engine check until the dictation has ended, for
/// services outside the dictation that must not interrupt it.
/// </summary>
internal interface IDictationState
{
    /// <summary>
    /// Gets whether a dictation is in progress. Readable from any thread.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Raised when <see cref="IsActive"/> changes, on the thread that changed it: the dictation's loop, or the thread
    /// that stops the application.
    /// </summary>
    event EventHandler? ActiveChanged;
}
