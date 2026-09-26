namespace Pisum.Transcribe.Dictation;

/// <summary>
/// The hotkey's availability on Windows, where neither reason of <see cref="HotkeyUnavailableReason"/> occurs.
/// </summary>
internal sealed class AlwaysAvailableHotkey : IHotkeyAvailability
{
    /// <inheritdoc />
    public HotkeyUnavailableReason? Reason => null;

    /// <inheritdoc />
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}
