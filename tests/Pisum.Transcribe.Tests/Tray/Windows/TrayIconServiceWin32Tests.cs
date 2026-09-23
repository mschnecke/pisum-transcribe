using Avalonia;
using Avalonia.Controls;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.Tray;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class TrayIconServiceWin32Tests
{
    [Fact]
    public Task Constructor_Windows_LabelsLastMenuItemExit()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Act
            var sut = CreateSut();

            // Assert
            ((NativeMenuItem) sut.Menu.Items[^1]).Header.ShouldBe("Exit");
            sut.Remove();
        });
    }

    [Fact]
    public Task WindowOpened_TrayPopup_UpdatesMenuItems()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var header = "Before";
            var sut = CreateSut();
            sut.AddMenuItem(() => header, () => { });
            sut.UpdateMenuItems();
            header = "After";
            var popup = new TrayPopupRoot();

            // Act
            popup.Show();

            // Assert
            ((NativeMenuItem) sut.Menu.Items[0]).Header.ShouldBe("After");
            popup.Close();
            sut.Remove();
        });
    }

    [Fact]
    public Task WindowOpened_OtherWindow_LeavesMenuItemsAlone()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var header = "Before";
            var sut = CreateSut();
            sut.AddMenuItem(() => header, () => { });
            sut.UpdateMenuItems();
            header = "After";
            var window = new Window();

            // Act
            window.Show();

            // Assert
            ((NativeMenuItem) sut.Menu.Items[0]).Header.ShouldBe("Before");
            window.Close();
            sut.Remove();
        });
    }

    [Fact]
    public void TrayPopupTypeName_AvaloniaWin32_NamesItsTrayMenuWindow()
    {
        // Act
        var types = typeof(Win32PlatformOptions).Assembly.GetTypes()
            .Where(type => type.Name == TrayIconService.TrayPopupTypeName);

        // Assert: an Avalonia update that renames the type would freeze the tray menu unnoticed.
        types.ShouldHaveSingleItem().IsSubclassOf(typeof(Window)).ShouldBeTrue();
    }

    private static TrayIconService CreateSut()
    {
        return new TrayIconService(new FakeTrayIconSet(), new InlineUiDispatcher());
    }

    /// <summary>
    /// A window with the type name of the tray menu's popup in Avalonia's Win32 backend.
    /// </summary>
    private sealed class TrayPopupRoot : Window;
}
