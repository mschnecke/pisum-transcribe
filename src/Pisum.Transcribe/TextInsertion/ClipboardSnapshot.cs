namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// A copy of the clipboard contents, taken before a paste to restore them afterwards.
/// </summary>
/// <param name="Data">
/// The copied formats, without the history-exclusion and restore markers. Empty when the clipboard was empty.
/// </param>
/// <param name="IsSensitive">
/// Whether the source marked the content as excluded from clipboard monitoring, clipboard history or the cloud
/// clipboard, as password managers do. Sensitive content is never restored.
/// </param>
internal sealed record ClipboardSnapshot(DataObject Data, bool IsSensitive);
