using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Pisum.Transcribe.Dictation;

/// <summary>
/// The recording overlay on Windows: the monitor through <c>MonitorFromWindow</c>, and the extended window styles.
/// </summary>
internal sealed class Win32OverlayPlatform : IOverlayPlatform
{
    /// <summary>
    /// The extended window styles that the overlay needs: no activation, click-through (with layered), and no Alt+Tab
    /// entry.
    /// </summary>
    public const WINDOW_EX_STYLE ExtendedStyles = WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TRANSPARENT |
                                                  WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_LAYERED;

    /// <inheritdoc />
    public PixelRect GetWorkArea(nint targetWindow, out uint dpi)
    {
        var monitor = PInvoke.MonitorFromWindow((HWND) targetWindow,
            targetWindow == 0 ? MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY : MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO {cbSize = (uint) Marshal.SizeOf<MONITORINFO>()};
        PInvoke.GetMonitorInfo(monitor, ref info);

        // Needs Windows 8.1. The analyzer assumes the Windows 7 of the target framework, but .NET 10 needs Windows 10.
#pragma warning disable CA1416
        PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out dpi, out _);
#pragma warning restore CA1416

        var work = info.rcWork;
        return new PixelRect(work.left, work.top, work.right - work.left, work.bottom - work.top);
    }

    /// <inheritdoc />
    public void Configure(Window overlay)
    {
        // The headless platform of the tests has no window handle.
        if (overlay.TryGetPlatformHandle() is not {HandleDescriptor: "HWND"} handle)
        {
            return;
        }

        var window = (HWND) handle.Handle;
        var styles = (WINDOW_EX_STYLE) (uint) PInvoke.GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (nint) (uint) (styles | ExtendedStyles));
    }
}
