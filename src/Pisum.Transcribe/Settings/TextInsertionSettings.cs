using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Settings;

/// <summary>
/// The text insertion settings, saved as the <c>textInsertion</c> section.
/// </summary>
/// <param name="Method">How the transcript is delivered to the target window.</param>
/// <param name="RestoreClipboard">
/// Whether <see cref="InsertionMethod.ClipboardPaste"/> puts the previous clipboard contents back after the paste.
/// </param>
internal sealed record TextInsertionSettings(
    InsertionMethod Method = InsertionMethod.ClipboardPaste,
    bool RestoreClipboard = true);
