namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// The Windows clipboard. The <c>Try*Async</c> methods return <see langword="false"/> or <see langword="null"/> when
/// another process keeps the clipboard open for about 1 second.
/// </summary>
internal interface IClipboardService
{
    /// <summary>
    /// The clipboard sequence number, which Windows increments on every clipboard change. Readable from any thread.
    /// </summary>
    uint SequenceNumber { get; }

    /// <summary>
    /// Copies the current clipboard contents, best-effort for formats other than text.
    /// </summary>
    /// <returns>The snapshot, or <see langword="null"/> if the clipboard was busy.</returns>
    Task<ClipboardSnapshot?> TrySnapshotAsync();

    /// <summary>
    /// Places text on the clipboard as Unicode text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="excludeFromHistory">
    /// Whether to keep the text out of clipboard monitoring, clipboard history and the cloud clipboard.
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
