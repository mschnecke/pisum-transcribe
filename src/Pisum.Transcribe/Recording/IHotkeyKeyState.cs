namespace Pisum.Transcribe.Recording;

/// <summary>
/// Reads whether a hotkey key is still observably held, for the missed-release check of
/// <see cref="SharpHookPushToTalkHotkey"/>.
/// </summary>
internal interface IHotkeyKeyState
{
    /// <summary>
    /// Returns whether the key is held and its release could be seen by the keyboard hook.
    /// </summary>
    /// <param name="rawCode">The hook's raw code of the key: the Windows virtual-key code, or the macOS key code.</param>
    /// <returns><see langword="true"/> while the key reads as held.</returns>
    bool IsHeld(int rawCode);
}
