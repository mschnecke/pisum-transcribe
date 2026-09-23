namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// A copy of the clipboard contents, taken before a paste to restore them afterwards. Each clipboard implementation
/// derives its own type, because what a clipboard holds differs per platform.
/// </summary>
/// <param name="IsSensitive">
/// Whether the source marked the content as excluded from clipboard monitoring, clipboard history or the cloud
/// clipboard, as password managers do. Sensitive content is never restored.
/// </param>
internal abstract record ClipboardSnapshot(bool IsSensitive);
