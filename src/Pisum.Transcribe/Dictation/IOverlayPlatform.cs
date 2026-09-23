using Avalonia;
using Avalonia.Controls;

namespace Pisum.Transcribe.Dictation;

/// <summary>
/// What the recording overlay needs from the platform: the monitor it goes on, and the native window settings that
/// Avalonia doesn't expose. Call the members on the UI thread.
/// </summary>
internal interface IOverlayPlatform
{
    /// <summary>
    /// Reads the work area and DPI of the monitor that contains most of the target window, or of the primary monitor
    /// without a target.
    /// </summary>
    /// <param name="targetWindow">The target window's handle, or 0.</param>
    /// <param name="dpi">The monitor's effective DPI, 96 at 100 % scaling.</param>
    /// <returns>The monitor's work area in physical pixels.</returns>
    PixelRect GetWorkArea(nint targetWindow, out uint dpi);

    /// <summary>
    /// Applies the native settings to the shown overlay: it never activates, lets mouse clicks through, and is absent
    /// from the window switcher. Runs after each <see cref="Window.Show()"/>.
    /// </summary>
    /// <param name="overlay">The overlay window.</param>
    void Configure(Window overlay);
}
