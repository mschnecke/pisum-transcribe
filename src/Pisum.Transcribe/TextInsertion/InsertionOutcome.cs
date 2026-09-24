namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// How an insertion ended. Every outcome except <see cref="Inserted"/> and <see cref="ClipboardUnavailable"/> leaves the
/// transcript on the clipboard.
/// </summary>
internal enum InsertionOutcome
{
    /// <summary>
    /// The keystrokes were sent to the target window.
    /// </summary>
    Inserted,

    /// <summary>
    /// The target window was not in the foreground, or no window was when the recording started.
    /// </summary>
    TargetWindowChanged,

    /// <summary>
    /// The target window belongs to an elevated process, which would silently reject the input.
    /// </summary>
    TargetWindowElevated,

    /// <summary>
    /// macOS's Secure Event Input was on right before the keystrokes, for example because a password field has focus.
    /// </summary>
    SecureInputOn,

    /// <summary>
    /// A modifier key that would change the keystrokes was still held after the wait.
    /// </summary>
    ModifierKeysHeld,

    /// <summary>
    /// A fallback could not place the transcript on the clipboard, so it was not delivered.
    /// </summary>
    ClipboardUnavailable,
}
