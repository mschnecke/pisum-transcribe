namespace Pisum.Transcribe.Permissions;

/// <summary>
/// A permission that Pisum Transcribe needs on macOS.
/// </summary>
internal enum Permission
{
    /// <summary>
    /// Accessibility, for the push-to-talk hotkey and text insertion. Required.
    /// </summary>
    Accessibility,

    /// <summary>
    /// The microphone. Required.
    /// </summary>
    Microphone,

    /// <summary>
    /// Notifications. Optional.
    /// </summary>
    Notifications,

    /// <summary>
    /// Reading the pasteboard without asking, macOS's pasteboard privacy. Optional.
    /// </summary>
    PasteFromOtherApps,
}
