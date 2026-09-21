using System.Windows;
using Pisum.Transcribe.Dictation;

namespace Pisum.Transcribe.Tests.Dictation;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class RecordingOverlayWindowTests
{
    [Fact]
    public void CalculateBounds_100PercentScaling_PlacesOverlayBottomCenterAboveTaskbar()
    {
        // Arrange: a 1920 × 1080 monitor with a 48 px taskbar at the bottom.
        var workArea = new Int32Rect(0, 0, 1920, 1032);

        // Act
        var bounds = RecordingOverlayWindow.CalculateBounds(workArea, 96);

        // Assert
        bounds.ShouldBe(new Int32Rect(850, 940, 220, 44));
    }

    [Fact]
    public void CalculateBounds_SecondMonitorAt150Percent_ScalesSizeAndMarginToItsDpi()
    {
        // Arrange: a 2560 × 1440 monitor right of the primary monitor.
        var workArea = new Int32Rect(1920, 0, 2560, 1380);

        // Act
        var bounds = RecordingOverlayWindow.CalculateBounds(workArea, 144);

        // Assert: 330 × 66 px, 72 px above the work-area bottom.
        bounds.ShouldBe(new Int32Rect(3035, 1242, 330, 66));
    }
}
