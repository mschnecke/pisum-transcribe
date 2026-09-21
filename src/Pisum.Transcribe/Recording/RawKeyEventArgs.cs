using SharpHook.Data;

namespace Pisum.Transcribe.Recording;

/// <summary>
/// A key event while the push-to-talk hotkey is suspended, for recording a new hotkey.
/// </summary>
/// <param name="key">The key.</param>
/// <param name="isPressed"><see langword="true"/> for a key-down, <see langword="false"/> for a key-up.</param>
internal sealed class RawKeyEventArgs(KeyCode key, bool isPressed) : EventArgs
{
    /// <summary>
    /// The key.
    /// </summary>
    public KeyCode Key { get; } = key;

    /// <summary>
    /// <see langword="true"/> for a key-down, <see langword="false"/> for a key-up.
    /// </summary>
    public bool IsPressed { get; } = isPressed;
}
