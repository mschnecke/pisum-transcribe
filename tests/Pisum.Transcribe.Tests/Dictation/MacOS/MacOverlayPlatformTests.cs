using Avalonia;
using Pisum.Transcribe.Dictation;

namespace Pisum.Transcribe.Tests.Dictation;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class MacOverlayPlatformTests
{
    // The development Mac's built-in display (placement spike), and a second display to its left, higher up.
    private static readonly (PixelRect Bounds, PixelRect WorkingArea, bool IsPrimary) BuiltIn =
        (new PixelRect(0, 0, 1470, 956), new PixelRect(0, 33, 1470, 866), true);

    private static readonly (PixelRect Bounds, PixelRect WorkingArea, bool IsPrimary) LeftDisplay =
        (new PixelRect(-1920, -300, 1920, 1080), new PixelRect(-1920, -275, 1920, 1055), false);

    private static readonly IReadOnlyList<(PixelRect Bounds, PixelRect WorkingArea, bool IsPrimary)> Screens =
        [LeftDisplay, BuiltIn];

    [Fact]
    public void ChooseWorkArea_FrameOnPrimaryScreen_ReturnsItsWorkingArea()
    {
        // Act
        var workArea = MacOverlayPlatform.ChooseWorkArea(Screens, new Rect(148, 72, 652, 420));

        // Assert
        workArea.ShouldBe(BuiltIn.WorkingArea);
    }

    [Fact]
    public void ChooseWorkArea_FrameOnSecondScreenWithNegativeOrigin_ReturnsItsWorkingArea()
    {
        // Act
        var workArea = MacOverlayPlatform.ChooseWorkArea(Screens, new Rect(-1500, -200, 800, 600));

        // Assert
        workArea.ShouldBe(LeftDisplay.WorkingArea);
    }

    [Fact]
    public void ChooseWorkArea_FrameCenterOffEveryScreen_ReturnsPrimaryWorkingArea()
    {
        // Act
        var workArea = MacOverlayPlatform.ChooseWorkArea(Screens, new Rect(5000, 5000, 400, 300));

        // Assert
        workArea.ShouldBe(BuiltIn.WorkingArea);
    }

    [Fact]
    public void ChooseWorkArea_WindowMostlyOffItsScreen_UsesTheScreenOfItsCenter()
    {
        // Act: the frame starts on the left display, and its center is on the built-in one.
        var workArea = MacOverlayPlatform.ChooseWorkArea(Screens, new Rect(-300, 100, 1000, 400));

        // Assert
        workArea.ShouldBe(BuiltIn.WorkingArea);
    }

    [Fact]
    public void ChooseWorkArea_NoFrame_ReturnsPrimaryWorkingArea()
    {
        // Act
        var workArea = MacOverlayPlatform.ChooseWorkArea(Screens, null);

        // Assert
        workArea.ShouldBe(BuiltIn.WorkingArea);
    }

    [Fact]
    public Task GetWorkArea_HeadlessWithoutTracker_ReturnsPrimaryWorkingAreaAt96Dpi()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var sut = new MacOverlayPlatform(null, null);
            var overlay = new RecordingOverlayWindow(sut);

            // Act
            var workArea = sut.GetWorkArea(overlay, 1, out var dpi);

            // Assert
            dpi.ShouldBe(96u);
            workArea.ShouldBe(overlay.Screens.Primary!.WorkingArea);
            overlay.Close();
        });
    }
}
