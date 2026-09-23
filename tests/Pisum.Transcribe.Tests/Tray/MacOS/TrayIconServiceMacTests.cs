using Avalonia.Controls;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.Tray;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class TrayIconServiceMacTests
{
    [Fact]
    public Task Constructor_MacOS_LabelsLastMenuItemQuitPisumTranscribe()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Act
            var sut = new TrayIconService(new FakeTrayIconSet(), new InlineUiDispatcher());

            // Assert
            ((NativeMenuItem) sut.Menu.Items[^1]).Header.ShouldBe("Quit Pisum Transcribe");
            sut.Remove();
        });
    }
}
