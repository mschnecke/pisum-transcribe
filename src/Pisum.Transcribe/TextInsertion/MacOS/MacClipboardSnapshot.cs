namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// A copy of the macOS pasteboard: every item with every type that <see cref="MacClipboardService"/> copied.
/// </summary>
/// <param name="Items">The items in their order, each a list of its types. Empty when the pasteboard was empty.</param>
/// <param name="IsSensitive">
/// <inheritdoc cref="ClipboardSnapshot" path="/param[@name='IsSensitive']"/>
/// </param>
internal sealed record MacClipboardSnapshot(
    IReadOnlyList<IReadOnlyList<MacPasteboardEntry>> Items,
    bool IsSensitive) : ClipboardSnapshot(IsSensitive);
