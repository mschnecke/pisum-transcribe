namespace Pisum.Transcribe.Tray;

/// <summary>
/// The mode of the taskbar, which decides the color of the tray icon at rest.
/// </summary>
internal enum TaskbarMode
{
    /// <summary>
    /// A light taskbar, on which the icon is dark.
    /// </summary>
    Light,

    /// <summary>
    /// A dark taskbar, on which the icon is light.
    /// </summary>
    Dark,
}
