using Windows.Win32;

namespace Pisum.Transcribe.Recording;

/// <summary>
/// Reads the key state through <c>GetAsyncKeyState</c>.
/// </summary>
internal sealed class WindowsHotkeyKeyState : IHotkeyKeyState
{
    /// <inheritdoc />
    public bool IsHeld(int rawCode)
    {
        // Reads as up when UIPI blocks access to the foreground window or another desktop is active.
        return (PInvoke.GetAsyncKeyState(rawCode) & 0x8000) != 0;
    }
}
