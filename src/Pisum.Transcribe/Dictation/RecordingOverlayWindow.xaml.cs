using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
internal sealed partial class RecordingOverlayWindow : IRecordingOverlay
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

    private const double DefaultDpi = 96;

    private static readonly TimeSpan ElapsedInterval = TimeSpan.FromMilliseconds(250);
    private static readonly Brush StartingBrush = CreateBrush(0x9E, 0x9E, 0x9E);
    private static readonly Brush RecordingBrush = CreateBrush(0xE5, 0x39, 0x35);

    private readonly DispatcherTimer _elapsedTimer = new() {Interval = ElapsedInterval};
    private readonly Stopwatch _recording = new();
    private readonly DoubleAnimation _spin = new(0, 360, TimeSpan.FromSeconds(1))
    {
        RepeatBehavior = RepeatBehavior.Forever,
    };

    /// <summary>
    /// Initializes a new instance and creates its window handle, hidden. Create it on the UI thread.
    /// </summary>
    public RecordingOverlayWindow()
    {
        InitializeComponent();
        _elapsedTimer.Tick += (_, _) => UpdateElapsed();

        // Created now, so the extended styles are set and the window can be placed before it is first shown.
        new WindowInteropHelper(this).EnsureHandle();
    }

    /// <summary>
    /// Calculates where the overlay goes: bottom center of a work area, scaled to the monitor's DPI.
    /// </summary>
    /// <param name="workArea">The monitor's work area in physical pixels.</param>
    /// <param name="dpi">The monitor's effective DPI, 96 at 100 % scaling.</param>
    /// <returns>The overlay bounds in physical pixels.</returns>
    public static Int32Rect CalculateBounds(Int32Rect workArea, uint dpi)
    {
        var scale = dpi / DefaultDpi;
        var width = (int) Math.Round(OverlayWidth * scale);
        var height = (int) Math.Round(OverlayHeight * scale);
        var bottomMargin = (int) Math.Round(BottomMargin * scale);
        return new Int32Rect(workArea.X + (workArea.Width - width) / 2,
            workArea.Y + workArea.Height - bottomMargin - height, width, height);
    }

    /// <inheritdoc />
    public void ShowStarting(nint targetWindow)
    {
        ShowContent(StartingBrush, false, null);

        var bounds = CalculateBounds(GetWorkArea(targetWindow, out var dpi), dpi);
        MoveTo(bounds.X, bounds.Y);
        Show();

        // A window that moved to a monitor with another DPI was rescaled by WPF, which can shift it.
        MoveTo(bounds.X, bounds.Y);
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

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // No activation, click-through (with layered), and no Alt+Tab entry.
        var window = (HWND) new WindowInteropHelper(this).Handle;
        var styles = (WINDOW_EX_STYLE) (uint) PInvoke.GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        styles |= WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TRANSPARENT |
                  WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_LAYERED;
        PInvoke.SetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (nint) (uint) styles);
    }

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Reads the work area and DPI of the monitor that contains most of the target window, or of the primary monitor
    /// without a target.
    /// </summary>
    private static Int32Rect GetWorkArea(nint targetWindow, out uint dpi)
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
        return new Int32Rect(work.left, work.top, work.right - work.left, work.bottom - work.top);
    }

    private void MoveTo(int x, int y)
    {
        // Left and Top are device-independent units of the monitor the window is on now.
        var scale = VisualTreeHelper.GetDpi(this);
        Left = x / scale.DpiScaleX;
        Top = y / scale.DpiScaleY;
    }

    /// <summary>
    /// Sets the dot, the spinner and the label. <see langword="null"/> hides an element; any call stops the elapsed time.
    /// </summary>
    private void ShowContent(Brush? dot, bool spinner, string? text)
    {
        _elapsedTimer.Stop();
        Dot.Fill = dot;
        Dot.Visibility = dot is null ? Visibility.Collapsed : Visibility.Visible;
        Spinner.Visibility = spinner ? Visibility.Visible : Visibility.Collapsed;
        SpinnerRotation.BeginAnimation(RotateTransform.AngleProperty, spinner ? _spin : null);
        Label.Text = text ?? string.Empty;
        Label.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        Label.Margin = new Thickness(dot is null && !spinner ? 0 : 10, 0, 0, 0);
    }

    private void UpdateElapsed()
    {
        Label.Text = $@"{DictationMessages.OverlayRecording} {_recording.Elapsed:m\:ss}";
    }
}
