using System.Drawing;
using System.Drawing.Drawing2D;

namespace Pisum.Transcribe.Dictation;

/// <summary>
/// The tray icons of the dictation states, drawn once at startup from the app icon with a coloured dot in
/// its bottom-right corner. Real artwork can replace this class without changing <see cref="DictationFeedback"/>.
/// </summary>
/// <remarks>
/// The icons live for the application's lifetime, so their icon handles are never released.
/// </remarks>
internal sealed class DictationIcons
{
    /// <summary>
    /// The width and height of the icons in pixels. Windows scales them for the notification area.
    /// </summary>
    public const int Size = 32;

    private const string IconResourceName = "Pisum.Transcribe.Tray.TrayIcon.ico";

    /// <summary>
    /// Initializes a new instance and draws the icons.
    /// </summary>
    public DictationIcons()
    {
        using var iconStream = typeof(DictationIcons).Assembly.GetManifestResourceStream(IconResourceName)
                               ?? throw new InvalidOperationException(
                                   $"Embedded resource {IconResourceName} is missing.");
        Ready = new Icon(iconStream, Size, Size);
        Recording = WithDot(Ready, Color.FromArgb(0xE5, 0x39, 0x35));
        Transcribing = WithDot(Ready, Color.FromArgb(0xFF, 0xB3, 0x00));
        Unavailable = WithDot(Ready, Color.FromArgb(0x9E, 0x9E, 0x9E));
    }

    /// <summary>
    /// Ready to dictate: the app icon unchanged.
    /// </summary>
    public Icon Ready { get; }

    /// <summary>
    /// Recording: a red dot.
    /// </summary>
    public Icon Recording { get; }

    /// <summary>
    /// Transcribing: an amber dot.
    /// </summary>
    public Icon Transcribing { get; }

    /// <summary>
    /// No model, loading or failed: a grey dot.
    /// </summary>
    public Icon Unavailable { get; }

    private static Icon WithDot(Icon icon, Color color)
    {
        using var bitmap = icon.ToBitmap();
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // A white ring keeps the dot visible on the icon and on dark and light taskbars.
            using var ring = new SolidBrush(Color.White);
            using var dot = new SolidBrush(color);
            graphics.FillEllipse(ring, 15, 15, 17, 17);
            graphics.FillEllipse(dot, 17, 17, 13, 13);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
