namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// The system clipboard: the Windows clipboard, or the general pasteboard on macOS. The <c>Try*Async</c> methods
/// return <see langword="false"/> or <see langword="null"/> when the clipboard can't be used right now, and the
/// implementation logs why: on Windows, another process keeps the clipboard open for about 1 second; on macOS, the
/// helper library isn't available, or, for a snapshot only, the system doesn't let the application read the pasteboard
/// without asking the user.
/// </summary>
internal interface IClipboardService
{
    /// <summary>
    /// A number that changes on every clipboard change: the clipboard sequence number on Windows, the pasteboard's
    /// change count on macOS. Readable from any thread.
    /// </summary>
    long SequenceNumber { get; }

    /// <summary>
    /// Copies the current clipboard contents, best-effort for formats other than text.
    /// </summary>
    /// <returns>The snapshot, or <see langword="null"/> if the clipboard could not be read.</returns>
    Task<ClipboardSnapshot?> TrySnapshotAsync();

    /// <summary>
    /// Places text on the clipboard as Unicode text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="excludeFromHistory">
    /// Whether to keep the text out of clipboard monitoring, clipboard history and the cloud clipboard, or on macOS out
    /// of clipboard managers and Universal Clipboard.
    /// </param>
    /// <returns><see langword="true"/> if the text is on the clipboard.</returns>
    Task<bool> TrySetTextAsync(string text, bool excludeFromHistory);

    /// <summary>
    /// Puts a snapshot back on the clipboard, marked so that it is not added to clipboard history or the cloud
    /// clipboard a second time. An empty snapshot clears the clipboard.
    /// </summary>
    /// <param name="snapshot">The snapshot to restore.</param>
    /// <returns><see langword="true"/> if the snapshot is on the clipboard.</returns>
    Task<bool> TryRestoreAsync(ClipboardSnapshot snapshot);
}
