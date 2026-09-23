using Avalonia.Controls;

namespace Pisum.Transcribe.Tray;

/// <summary>
/// The menu bar icons on macOS, from the PNGs in <c>Tray/MacOS</c>: template images for ready and unavailable, which
/// macOS draws in the menu bar's color, and the red and amber glyphs for recording and transcribing. They never change,
/// because macOS follows light and dark mode itself.
/// </summary>
/// <remarks>
/// Avalonia hands the PNG to <c>NSImage</c> and scales it to the menu bar's height, so the <c>@2x</c> image serves both
/// scales.
/// </remarks>
internal sealed class MacTrayIconSet : ITrayIconSet
{
    private const string ResourcePrefix = "Pisum.Transcribe.Tray.MacOS.TrayGlyph.";

    private readonly TrayIconImage _ready = Load("ReadyTemplate", true);
    private readonly TrayIconImage _unavailable = Load("UnavailableTemplate", true);
    private readonly TrayIconImage _recording = Load("Recording", false);
    private readonly TrayIconImage _transcribing = Load("Transcribing", false);

    /// <inheritdoc />
    /// <remarks>Never raised.</remarks>
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    /// <remarks>The ready glyph, because a colored app icon doesn't belong in the menu bar.</remarks>
    public TrayIconImage Initial => _ready;

    /// <inheritdoc />
    public TrayIconImage For(TrayStatus status)
    {
        return status switch
        {
            TrayStatus.Ready => _ready,
            TrayStatus.Unavailable => _unavailable,
            TrayStatus.Recording => _recording,
            TrayStatus.Transcribing => _transcribing,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
    }

    private static TrayIconImage Load(string name, bool isTemplate)
    {
        var resourceName = $"{ResourcePrefix}{name}@2x.png";
        using var stream = typeof(MacTrayIconSet).Assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Embedded resource {resourceName} is missing.");
        return new TrayIconImage(new WindowIcon(stream), isTemplate);
    }
}
