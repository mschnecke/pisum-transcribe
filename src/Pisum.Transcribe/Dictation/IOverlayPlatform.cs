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
    /// Reads the work area of the monitor that contains the target window, or of the primary monitor without a target,
    /// and the DPI that the overlay's size is scaled by.
    /// </summary>
    /// <param name="overlay">The overlay window, whose screens the platform may use.</param>
    /// <param name="targetWindow">The target's window value from <see cref="TextInsertion.InsertionTarget"/>, or 0.</param>
    /// <param name="dpi">
    /// The DPI that the overlay's size is scaled by, 96 for none: the monitor's effective DPI on Windows, and always 96
    /// on macOS, which scales for Retina itself.
    /// </param>
    /// <returns>
    /// The work area in <see cref="Window.Position"/>'s units: physical pixels on Windows, points on macOS (design D2 of
    /// add-macos-dictation).
    /// </returns>
    PixelRect GetWorkArea(Window overlay, nint targetWindow, out uint dpi);

    /// <summary>
    /// Applies the native settings to the overlay: it never activates, lets mouse clicks through, and is absent from the
    /// window switcher. Runs right before and right after each <see cref="Window.Show()"/>, so it must be idempotent.
    /// </summary>
    /// <param name="overlay">The overlay window.</param>
    void Configure(Window overlay);
}
