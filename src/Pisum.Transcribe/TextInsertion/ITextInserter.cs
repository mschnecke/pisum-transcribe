using Pisum.Transcribe.Settings;

namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// Delivers a transcript at the cursor of the target window.
/// </summary>
internal interface ITextInserter
{
    /// <summary>
    /// Inserts the text into the target window if it is still in the foreground. Otherwise, and whenever the input
    /// could go wrong, leaves the text on the clipboard and reports why.
    /// </summary>
    /// <param name="text">The transcript.</param>
    /// <param name="target">The window captured when the recording started.</param>
    /// <param name="settings">The settings the dictation started with.</param>
    /// <param name="cancellationToken">
    /// A token to cancel the wait for released modifier keys. A cancelled wait leaves the text on the clipboard and
    /// throws <see cref="OperationCanceledException"/>.
    /// </param>
    /// <returns>How the insertion ended.</returns>
    Task<InsertionOutcome> InsertAsync(string text,
                                       InsertionTarget target,
                                       TextInsertionSettings settings,
                                       CancellationToken cancellationToken);
}
