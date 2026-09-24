namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// One pasteboard through the Swift helper. Called only on <see cref="MacClipboardService"/>'s pasteboard thread,
/// except <see cref="ChangeCount"/>.
/// </summary>
internal interface IMacPasteboard
{
    /// <summary>
    /// The change count, which rises with every write. Never alerts, and readable from any thread.
    /// </summary>
    long ChangeCount { get; }

    /// <summary>
    /// How the system lets this app read the pasteboard: -1 without pasteboard privacy (before macOS 15.4, or a named
    /// pasteboard), otherwise 0 default, 1 asks each time, 2 always allowed, 3 always denied.
    /// </summary>
    int AccessBehavior { get; }

    /// <summary>
    /// Copies every item with every type except file promises. Reads the pasteboard, so it may alert unless
    /// <see cref="AccessBehavior"/> is -1 or 2.
    /// </summary>
    /// <returns>
    /// The snapshot buffer in the format of <see cref="MacPasteboardFormat"/>, or <see langword="null"/> on failure.
    /// </returns>
    byte[]? Snapshot();

    /// <summary>
    /// Replaces the contents with text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="exclude">Whether to keep it on this Mac and mark it for clipboard managers.</param>
    /// <returns><see langword="true"/> if the text is on the pasteboard.</returns>
    bool SetText(string text, bool exclude);

    /// <summary>
    /// Replaces the contents with the items of a snapshot buffer, kept on this Mac and marked as transient and
    /// restored. An empty snapshot clears the pasteboard.
    /// </summary>
    /// <param name="buffer">The snapshot buffer.</param>
    /// <returns><see langword="true"/> if the items are on the pasteboard.</returns>
    bool Restore(byte[] buffer);
}
