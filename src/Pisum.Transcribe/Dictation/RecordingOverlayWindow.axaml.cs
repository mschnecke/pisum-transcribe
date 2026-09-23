using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

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
    /// The style class that turns the spinner.
    /// </summary>
    internal const string SpinningClass = "spinning";

    private const double DefaultDpi = 96;

    private static readonly TimeSpan ElapsedInterval = TimeSpan.FromMilliseconds(250);
    private static readonly IBrush StartingBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
    private static readonly IBrush RecordingBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));

    private readonly DispatcherTimer _elapsedTimer = new() {Interval = ElapsedInterval};
    private readonly Stopwatch _recording = new();
    private readonly IOverlayPlatform _platform;

    /// <summary>
    /// Initializes a new instance, hidden, with the platform's placement and window settings, for the XAML loader.
    /// Create it on the UI thread.
    /// </summary>
    public RecordingOverlayWindow()
        : this(CreatePlatform())
    {
    }

    /// <summary>
    /// Initializes a new instance, hidden. Create it on the UI thread.
    /// </summary>
    /// <param name="platform">The monitor placement and the native window settings.</param>
    public RecordingOverlayWindow(IOverlayPlatform platform)
    {
        _platform = platform;
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

        var bounds = CalculateBounds(_platform.GetWorkArea(targetWindow, out var dpi), dpi);
        Position = bounds.Position;
        Show();

        // Avalonia resets the extended styles on every Show, so they are set again each time. ShowActivated=false keeps
        // the focus in the target window until then.
        _platform.Configure(this);

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

    private static IOverlayPlatform CreatePlatform()
    {
#if WINDOWS
        return new Win32OverlayPlatform();
#else
        // add-macos-dictation adds the overlay on macOS.
        throw new PlatformNotSupportedException("The recording overlay is not available on this platform yet.");
#endif
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
