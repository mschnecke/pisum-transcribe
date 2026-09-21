namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// Sends simulated keystrokes to the foreground window and reads the modifier keys.
/// </summary>
internal interface IKeyboardInput
{
    /// <summary>
    /// Sends Ctrl+V.
    /// </summary>
    void SendPaste();

    /// <summary>
    /// Types the text as Unicode character input, with each line break sent as Enter.
    /// </summary>
    /// <param name="text">The text.</param>
    void TypeText(string text);

    /// <summary>
    /// Checks whether a modifier key that would change the keystrokes is held: Shift, Alt or Windows, and optionally
    /// Ctrl.
    /// </summary>
    /// <param name="includeControl">Whether a held Ctrl counts.</param>
    /// <returns><see langword="true"/> if one of these keys is down.</returns>
    bool AreModifiersDown(bool includeControl);
}
