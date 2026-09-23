using Avalonia;
using Avalonia.Controls;
using Pisum.Transcribe.Dictation;

namespace Pisum.Transcribe.Tests.Dictation;

/// <summary>
/// A 1920 × 1080 primary monitor at 100 % scaling, and no native window settings.
/// </summary>
internal sealed class FakeOverlayPlatform : IOverlayPlatform
{
    public PixelRect GetWorkArea(nint targetWindow, out uint dpi)
    {
        dpi = 96;
        return new PixelRect(0, 0, 1920, 1032);
    }

    public void Configure(Window overlay)
    {
    }
}
