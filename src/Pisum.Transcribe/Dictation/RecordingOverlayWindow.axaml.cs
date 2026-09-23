using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Pisum.Transcribe.Dictation;

/// <summary>
/// The recording overlay: a pill at the bottom center of the target window's monitor. It stays on top, never activates,
/// lets mouse clicks through, and is absent from the taskbar and Alt+Tab. It is created once and shown and hidden.
/// </summary>
internal sealed partial class RecordingOverlayWindow : Window, IRecordingOverlay
{
    /// <summary>
    /// The overlay width in device-independent pixels.
    /// </summary>
    public const double OverlayWidth = 220;

    /// <summary>
    /// The overlay height in device-independent pixels.
    /// </summary>
    public const double OverlayHeight = 44;

    /// <summary>
    /// The distance from the bottom of the work area in device-independent pixels.
    /// </summary>
    public const double BottomMargin = 48;

    /// <summary>
    /// The extended window styles that the overlay needs: no activation, click-through (with layered), and no Alt+Tab
    /// entry.
    /// </summary>
    public const WINDOW_EX_STYLE ExtendedStyles = WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TRANSPARENT |
                                                  WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_LAYERED;

    /// <summary>
    /// The style class that turns the spinner.
    /// </summary>
    internal const string SpinningClass = "spinning";

    private const double DefaultDpi = 96;

    private static readonly TimeSpan ElapsedInterval = TimeSpan.FromMilliseconds(250);
    private static readonly IBrush StartingBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
    private static readonly IBrush RecordingBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));

    private readonly DispatcherTimer _elapsedTimer = new() {Interval = ElapsedInterval};
    private readonly Stopwatch _recording = new();

    /// <summary>
    /// Initializes a new instance, hidden. Create it on the UI thread.
    /// </summary>
    public RecordingOverlayWindow()
    {
        InitializeComponent();
        _elapsedTimer.Tick += (_, _) => UpdateElapsed();
    }

    /// <summary>
    /// Calculates where the overlay goes: bottom center of a work area, scaled to the monitor's DPI.
    /// </summary>
    /// <param name="workArea">The monitor's work area in physical pixels.</param>
    /// <param name="dpi">The monitor's effective DPI, 96 at 100 % scaling.</param>
    /// <returns>The overlay bounds in physical pixels.</returns>
    public static PixelRect CalculateBounds(PixelRect workArea, uint dpi)
    {
        var scale = dpi / DefaultDpi;
        var width = (int) Math.Round(OverlayWidth * scale);
        var height = (int) Math.Round(OverlayHeight * scale);
        var bottomMargin = (int) Math.Round(BottomMargin * scale);
        return new PixelRect(workArea.X + (workArea.Width - width) / 2,
            workArea.Y + workArea.Height - bottomMargin - height, width, height);
    }

    /// <inheritdoc />
    public void ShowStarting(nint targetWindow)
    {
        ShowContent(StartingBrush, false, null);

        var bounds = CalculateBounds(GetWorkArea(targetWindow, out var dpi), dpi);
        Position = bounds.Position;
        Show();

        // Avalonia resets the extended styles on every Show, so they are set again each time. ShowActivated=false keeps
        // the focus in the target window until then.
        ApplyExtendedStyles();

        // A window that moved to a monitor with another DPI was rescaled by Avalonia, which can shift it.
        Position = bounds.Position;
    }

    /// <inheritdoc />
    public void ShowRecording()
    {
        ShowContent(RecordingBrush, false, string.Empty);
        _recording.Restart();
        UpdateElapsed();
        _elapsedTimer.Start();
    }

    /// <inheritdoc />
    public void ShowTranscribing()
    {
        ShowContent(null, true, DictationMessages.OverlayTranscribing);
    }

    /// <inheritdoc />
    public void ShowMessage(string text)
    {
        ShowContent(null, false, text);
    }

    /// <inheritdoc />
    void IRecordingOverlay.Hide()
    {
        ShowContent(null, false, null);
        Hide();
    }

    /// <summary>
    /// Reads the work area and DPI of the monitor that contains most of the target window, or of the primary monitor
    /// without a target.
    /// </summary>
    private static PixelRect GetWorkArea(nint targetWindow, out uint dpi)
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

    private void ApplyExtendedStyles()
    {
        // The headless platform of the tests has no window handle.
        if (TryGetPlatformHandle() is not {HandleDescriptor: "HWND"} handle)
        {
            return;
        }

        var window = (HWND) handle.Handle;
        var styles = (WINDOW_EX_STYLE) (uint) PInvoke.GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (nint) (uint) (styles | ExtendedStyles));
    }

    /// <summary>
    /// Sets the dot, the spinner and the label. <see langword="null"/> hides an element; any call stops the elapsed time.
    /// </summary>
    private void ShowContent(IBrush? dot, bool spinner, string? text)
    {
        _elapsedTimer.Stop();
        Dot.Fill = dot;
        Dot.IsVisible = dot is not null;
        Spinner.IsVisible = spinner;
        Spinner.Classes.Set(SpinningClass, spinner);
        Label.Text = text ?? string.Empty;
        Label.IsVisible = text is not null;
        Label.Margin = new Thickness(dot is null && !spinner ? 0 : 10, 0, 0, 0);
    }

    private void UpdateElapsed()
    {
        Label.Text = $@"{DictationMessages.OverlayRecording} {_recording.Elapsed:m\:ss}";
    }
}
