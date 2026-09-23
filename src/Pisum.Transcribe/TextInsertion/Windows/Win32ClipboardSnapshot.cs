namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// A copy of the Windows clipboard, as the raw bytes of every format that <see cref="Win32ClipboardService"/> copied.
/// </summary>
/// <param name="Formats">
/// The copied formats in the order the clipboard enumerated them, which is the order the source placed them. Empty
/// when the clipboard was empty. The history-exclusion formats and the restore marker are not among them.
/// </param>
/// <param name="IsSensitive">
/// <inheritdoc cref="ClipboardSnapshot" path="/param[@name='IsSensitive']"/>
/// </param>
internal sealed record Win32ClipboardSnapshot(
    IReadOnlyList<(uint Format, byte[] Bytes)> Formats,
    bool IsSensitive) : ClipboardSnapshot(IsSensitive);
