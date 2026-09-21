namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// How the transcript is delivered to the target window.
/// </summary>
internal enum InsertionMethod
{
    /// <summary>
    /// Places the transcript on the clipboard and sends Ctrl+V.
    /// </summary>
    ClipboardPaste,

    /// <summary>
    /// Sends the transcript as Unicode keyboard input, with line breaks as Enter. The clipboard is not touched.
    /// </summary>
    TypeText,
}
