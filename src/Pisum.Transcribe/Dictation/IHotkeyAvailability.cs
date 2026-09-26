namespace Pisum.Transcribe.Dictation;

/// <summary>
/// Why the push-to-talk hotkey can't work, which the tray shows while no dictation is in progress (design D4 of
/// add-macos-dictation). The hotkey always works on Windows.
/// </summary>
internal interface IHotkeyAvailability
{
    /// <summary>
    /// Gets why the hotkey can't work, or <see langword="null"/> when it can. Read it on the UI thread.
    /// </summary>
    HotkeyUnavailableReason? Reason { get; }

    /// <summary>
    /// Raised on the UI thread when <see cref="Reason"/> changes.
    /// </summary>
    event EventHandler? Changed;
}

/// <summary>
/// Why the push-to-talk hotkey can't work, in the order the tray names them.
/// </summary>
internal enum HotkeyUnavailableReason
{
    /// <summary>
    /// The Accessibility grant isn't in effect, so the keyboard hook doesn't run.
    /// </summary>
    AccessibilityNotInEffect,

    /// <summary>
    /// macOS's Secure Event Input is on, so the keyboard hook sees no keys.
    /// </summary>
    SecureInputOn,
}
