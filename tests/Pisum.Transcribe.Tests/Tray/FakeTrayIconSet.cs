using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.Tray;

/// <summary>
/// An <see cref="ITrayIconSet"/> with a distinct 1 × 1 icon per status, whose icons a test replaces. Ready and
/// unavailable are templates. Create it on the UI thread.
/// </summary>
internal sealed class FakeTrayIconSet : ITrayIconSet
{
    private readonly Dictionary<TrayStatus, TrayIconImage> _icons = Enum.GetValues<TrayStatus>().ToDictionary(
        status => status,
        status => CreateIcon(status is TrayStatus.Ready or TrayStatus.Unavailable));

    public event EventHandler? Changed;

    public TrayIconImage Initial { get; } = CreateIcon(false);

    public TrayIconImage For(TrayStatus status)
    {
        return _icons[status];
    }

    /// <summary>
    /// Replaces the icon of a status and raises <see cref="Changed"/>.
    /// </summary>
    public TrayIconImage Replace(TrayStatus status)
    {
        var icon = CreateIcon(_icons[status].IsTemplate);
        _icons[status] = icon;
        Changed?.Invoke(this, EventArgs.Empty);
        return icon;
    }

    private static TrayIconImage CreateIcon(bool isTemplate)
    {
        return new TrayIconImage(new WindowIcon(new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96))),
            isTemplate);
    }
}
