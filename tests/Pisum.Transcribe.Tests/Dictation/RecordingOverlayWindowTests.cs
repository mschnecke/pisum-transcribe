using Avalonia;
using Pisum.Transcribe.Dictation;

namespace Pisum.Transcribe.Tests.Dictation;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class RecordingOverlayWindowTests
{
    [Fact]
    public void CalculateBounds_100PercentScaling_PlacesOverlayBottomCenterAboveTaskbar()
    {
        // Arrange: a 1920 × 1080 monitor with a 48 px taskbar at the bottom.
        var workArea = new PixelRect(0, 0, 1920, 1032);

        // Act
        var bounds = RecordingOverlayWindow.CalculateBounds(workArea, 96);

        // Assert
        bounds.ShouldBe(new PixelRect(850, 940, 220, 44));
    }

    [Fact]
    public void CalculateBounds_SecondMonitorAt150Percent_ScalesSizeAndMarginToItsDpi()
    {
        // Arrange: a 2560 × 1440 monitor right of the primary monitor.
        var workArea = new PixelRect(1920, 0, 2560, 1380);

        // Act
        var bounds = RecordingOverlayWindow.CalculateBounds(workArea, 144);

        // Assert: 330 × 66 px, 72 px above the work-area bottom.
        bounds.ShouldBe(new PixelRect(3035, 1242, 330, 66));
    }

    [Fact]
    public Task ShowStarting_Always_ConfiguresRightBeforeAndRightAfterShow()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var platform = new FakeOverlayPlatform();
            var sut = new RecordingOverlayWindow(platform);

            // Act
            ((IRecordingOverlay) sut).ShowStarting(0);

            // Assert
            platform.ConfiguredWhileVisible.ShouldBe([false, true]);
            sut.Close();
        });
    }

    [Fact]
    public Task ShowStarting_PrimaryMonitor_ShowsGreyDotWithoutText()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var sut = new RecordingOverlayWindow(new FakeOverlayPlatform());

            // Act
            ((IRecordingOverlay) sut).ShowStarting(0);

            // Assert
            sut.IsVisible.ShouldBeTrue();
            sut.ShowActivated.ShouldBeFalse();
            sut.Topmost.ShouldBeTrue();
            sut.ShowInTaskbar.ShouldBeFalse();
            sut.Dot.IsVisible.ShouldBeTrue();
            sut.Spinner.IsVisible.ShouldBeFalse();
            sut.Label.IsVisible.ShouldBeFalse();
            sut.Close();
        });
    }

    [Fact]
    public Task ShowRecording_AfterStarting_ShowsRedDotAndElapsedTimeFromZero()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var sut = new RecordingOverlayWindow(new FakeOverlayPlatform());
            IRecordingOverlay overlay = sut;
            overlay.ShowStarting(0);
            var startingDot = sut.Dot.Fill;

            // Act
            overlay.ShowRecording();

            // Assert
            sut.Dot.IsVisible.ShouldBeTrue();
            sut.Dot.Fill.ShouldNotBe(startingDot);
            sut.Spinner.IsVisible.ShouldBeFalse();
            sut.Label.IsVisible.ShouldBeTrue();
            sut.Label.Text.ShouldBe($"{DictationMessages.OverlayRecording} 0:00");
            sut.Close();
        });
    }

    [Fact]
    public Task ShowTranscribing_WhileRecording_ShowsTurningSpinnerAndText()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var sut = new RecordingOverlayWindow(new FakeOverlayPlatform());
            IRecordingOverlay overlay = sut;
            overlay.ShowStarting(0);
            overlay.ShowRecording();

            // Act
            overlay.ShowTranscribing();

            // Assert
            sut.Dot.IsVisible.ShouldBeFalse();
            sut.Spinner.IsVisible.ShouldBeTrue();
            sut.Spinner.Classes.ShouldContain(RecordingOverlayWindow.SpinningClass);
            sut.Label.Text.ShouldBe(DictationMessages.OverlayTranscribing);
            sut.Close();
        });
    }

    [Fact]
    public Task ShowMessage_WhileTranscribing_ShowsOnlyTheTextAndStopsSpinner()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var sut = new RecordingOverlayWindow(new FakeOverlayPlatform());
            IRecordingOverlay overlay = sut;
            overlay.ShowStarting(0);
            overlay.ShowTranscribing();

            // Act
            overlay.ShowMessage(DictationMessages.OverlayNoSpeech);

            // Assert
            sut.Dot.IsVisible.ShouldBeFalse();
            sut.Spinner.IsVisible.ShouldBeFalse();
            sut.Spinner.Classes.ShouldNotContain(RecordingOverlayWindow.SpinningClass);
            sut.Label.Text.ShouldBe(DictationMessages.OverlayNoSpeech);
            sut.Label.Margin.Left.ShouldBe(0);
            sut.Close();
        });
    }

    [Fact]
    public Task Hide_WhileRecording_HidesWindowAndContent()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var sut = new RecordingOverlayWindow(new FakeOverlayPlatform());
            IRecordingOverlay overlay = sut;
            overlay.ShowStarting(0);
            overlay.ShowRecording();

            // Act
            overlay.Hide();

            // Assert
            sut.IsVisible.ShouldBeFalse();
            sut.Dot.IsVisible.ShouldBeFalse();
            sut.Label.IsVisible.ShouldBeFalse();
            sut.Close();
        });
    }
}
