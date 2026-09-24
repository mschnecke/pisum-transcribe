using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Dictation;

/// <summary>
/// The recording overlay on macOS (design D2 of add-macos-dictation): the screen from the target window's frame, which
/// the tracker read at the capture, and the native settings through the Swift helper.
/// </summary>
/// <remarks>
/// Avalonia.Native reports screens and window positions in points with the origin at the top-left of the primary
/// screen, the space of the Accessibility API's frames, and its working area is the screen without the menu bar and the
/// Dock (placement spike, 2026-09-24). Its <c>Screen.Scaling</c> reported 1 on a Retina display, so the DPI is always
/// 96 and macOS scales the overlay itself.
/// </remarks>
internal sealed class MacOverlayPlatform : IOverlayPlatform
{
    private const uint Dpi = 96;

    private readonly MacForegroundWindowTracker? _tracker;
    private readonly MacNativeLibrary? _library;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="tracker">
    /// Holds the target window's frame, or <see langword="null"/> to place the overlay on the primary screen.
    /// </param>
    /// <param name="library">Tells whether the helper may be called, or <see langword="null"/> to skip it.</param>
    public MacOverlayPlatform(MacForegroundWindowTracker? tracker, MacNativeLibrary? library)
    {
        _tracker = tracker;
        _library = library;
    }

    /// <summary>
    /// Picks the work area for the overlay: the screen that contains the center of the target window's frame, or the
    /// primary screen without a frame or when the center is off every screen.
    /// </summary>
    /// <param name="screens">The screens' bounds and working areas, in points.</param>
    /// <param name="frame">The target window's frame, or <see langword="null"/>.</param>
    /// <returns>The working area, or an empty rectangle without screens.</returns>
    public static PixelRect ChooseWorkArea(IReadOnlyList<(PixelRect Bounds, PixelRect WorkingArea, bool IsPrimary)> screens,
                                           Rect? frame)
    {
        if (frame is { } target)
        {
            var center = new PixelPoint((int) Math.Floor(target.Center.X), (int) Math.Floor(target.Center.Y));
            foreach (var screen in screens)
            {
                if (screen.Bounds.Contains(center))
                {
                    return screen.WorkingArea;
                }
            }
        }

        foreach (var screen in screens)
        {
            if (screen.IsPrimary)
            {
                return screen.WorkingArea;
            }
        }

        return screens.Count > 0 ? screens[0].WorkingArea : default;
    }

    /// <inheritdoc />
    public PixelRect GetWorkArea(Window overlay, nint targetWindow, out uint dpi)
    {
        dpi = Dpi;
        Rect? frame = _tracker is not null && _tracker.TryGetFrame(targetWindow, out var captured) ? captured : null;
        var screens = overlay.Screens.All
            .Select(screen => (screen.Bounds, screen.WorkingArea, screen.IsPrimary))
            .ToList();
        return ChooseWorkArea(screens, frame);
    }

    /// <inheritdoc />
    public void Configure(Window overlay)
    {
        // The headless platform of the tests has no NSWindow.
        if (_library is not {IsAvailable: true} ||
            overlay.TryGetPlatformHandle() is not IMacOSTopLevelPlatformHandle {NSWindow: not 0} handle)
        {
            return;
        }

        PisumMac.OverlayConfigure(handle.NSWindow);
    }
}
