namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// Posts keyboard events and reads the modifier keys, for <see cref="MacKeyboardInput"/>.
/// </summary>
internal interface IMacKeyEvents
{
    /// <summary>
    /// Posts one key down or key up with exactly the given modifier flags.
    /// </summary>
    /// <param name="keyCode">The macOS key code.</param>
    /// <param name="down"><see langword="true"/> for key down.</param>
    /// <param name="flags">The <c>CGEventFlags</c> of the event.</param>
    void PostKey(ushort keyCode, bool down, ulong flags);

    /// <summary>
    /// Posts a key down and a key up that carry the text, without modifier flags.
    /// </summary>
    /// <param name="text">At most <see cref="MacKeyboardInput.ChunkLength"/> UTF-16 units.</param>
    void PostText(string text);

    /// <summary>
    /// Reads the modifier flags of the physical keys.
    /// </summary>
    /// <returns>The <c>CGEventFlags</c> of the HID system state.</returns>
    ulong ReadFlags();
}
