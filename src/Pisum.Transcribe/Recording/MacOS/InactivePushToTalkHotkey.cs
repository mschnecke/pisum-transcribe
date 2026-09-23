using SharpHook.Data;

namespace Pisum.Transcribe.Recording;

/// <summary>
/// A push-to-talk hotkey that sees no keys, on macOS until the keyboard hook comes there. It accepts every call and
/// raises no events, so the settings window works and its hotkey editor records nothing.
/// </summary>
internal sealed class InactivePushToTalkHotkey : IPushToTalkHotkey
{
    /// <inheritdoc />
    /// <remarks>Never raised.</remarks>
    public event EventHandler? Pressed
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    /// <remarks>Never raised.</remarks>
    public event EventHandler? Released
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    /// <remarks>Never raised.</remarks>
    public event EventHandler? Cancelled
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    /// <remarks>Never raised.</remarks>
    public event EventHandler<RawKeyEventArgs>? RawKey
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public void SetHotkey(IReadOnlySet<KeyCode> hotkey)
    {
    }

    /// <inheritdoc />
    public void Suspend()
    {
    }

    /// <inheritdoc />
    public void Resume()
    {
    }
}
