using Avalonia.Controls;
using Avalonia.Threading;

namespace Pisum.Transcribe.Tests;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class HeadlessUiTests
{
    [Fact]
    public Task RunAsync_WindowShownAndClosed_RunsOnUiThread()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var text = new TextBlock {Text = "Hello"};
            var window = new Window {Content = text, Width = 200, Height = 100};

            // Act
            window.Show();
            var shown = window.IsVisible;
            var laidOut = text.Bounds.Width > 0;
            window.Close();

            // Assert
            Dispatcher.UIThread.CheckAccess().ShouldBeTrue();
            shown.ShouldBeTrue();
            laidOut.ShouldBeTrue();
            window.IsVisible.ShouldBeFalse();
        });
    }
}
