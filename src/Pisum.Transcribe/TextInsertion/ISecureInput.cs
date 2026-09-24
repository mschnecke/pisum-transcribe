namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// Reads whether macOS's Secure Event Input is on, as it is while a password field has focus or Terminal's Secure
/// Keyboard Entry is enabled. Keystrokes are then not sent, so a transcript never lands in a password field.
/// </summary>
internal interface ISecureInput
{
    /// <summary>
    /// Whether secure input is on right now. Always <see langword="false"/> on Windows. Readable from any thread.
    /// </summary>
    bool IsEnabled { get; }
}
