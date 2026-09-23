using Avalonia;
using Avalonia.Controls;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.Tray;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class TrayIconServiceTests
{
    [Fact]
    public Task IconFor_EachStatusAndMode_ReturnsMatchingIcon()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var icons = TrayIconService.LoadIcons();

            // Act
            var iconsByStatusAndMode = Enum.GetValues<TrayStatus>()
                .SelectMany(_ => Enum.GetValues<TaskbarMode>(), (status, mode) => (status, mode))
                .ToDictionary(key => key, key => TrayIconService.IconFor(key.status, key.mode, icons));

            // Assert
            iconsByStatusAndMode.Count.ShouldBe(8);
            iconsByStatusAndMode[(TrayStatus.Ready, TaskbarMode.Light)].ShouldBeSameAs(icons.ReadyLight);
            iconsByStatusAndMode[(TrayStatus.Ready, TaskbarMode.Dark)].ShouldBeSameAs(icons.ReadyDark);
            iconsByStatusAndMode[(TrayStatus.Unavailable, TaskbarMode.Light)].ShouldBeSameAs(icons.UnavailableLight);
            iconsByStatusAndMode[(TrayStatus.Unavailable, TaskbarMode.Dark)].ShouldBeSameAs(icons.UnavailableDark);
            iconsByStatusAndMode[(TrayStatus.Recording, TaskbarMode.Light)].ShouldBeSameAs(icons.Recording);
            iconsByStatusAndMode[(TrayStatus.Recording, TaskbarMode.Dark)].ShouldBeSameAs(icons.Recording);
            iconsByStatusAndMode[(TrayStatus.Transcribing, TaskbarMode.Light)].ShouldBeSameAs(icons.Transcribing);
            iconsByStatusAndMode[(TrayStatus.Transcribing, TaskbarMode.Dark)].ShouldBeSameAs(icons.Transcribing);
        });
    }

    [Fact]
    public Task Show_AfterConstructor_ShowsIconThatStartedHidden()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var sut = CreateSut();
            var shownBefore = sut.IsShown;

            // Act
            sut.Show();

            // Assert
            shownBefore.ShouldBeFalse();
            sut.IsShown.ShouldBeTrue();
            sut.Remove();
        });
    }

    [Fact]
    public Task SetStatus_Recording_ShowsItsIconAndToolTip()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var sut = CreateSut();

            // Act
            sut.SetStatus(TrayStatus.Recording, "Pisum Transcribe – Recording…");

            // Assert
            sut.ShownIcon.ShouldBeSameAs(sut.Icons.Recording);
            sut.ToolTip.ShouldBe("Pisum Transcribe – Recording…");
            sut.Remove();
        });
    }

    [Fact]
    public Task TaskbarModeChanged_WhileReady_AppliesIconForNewMode()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var taskbarMode = A.Fake<ITaskbarModeWatcher>();
            A.CallTo(() => taskbarMode.Current).Returns(TaskbarMode.Light);
            var sut = new TrayIconService(taskbarMode, new InlineUiDispatcher());
            sut.SetStatus(TrayStatus.Ready, "Pisum Transcribe – Ready (CPU)");
            A.CallTo(() => taskbarMode.Current).Returns(TaskbarMode.Dark);

            // Act
            taskbarMode.Changed += Raise.WithEmpty();

            // Assert
            sut.ShownIcon.ShouldBeSameAs(sut.Icons.ReadyDark);
            sut.Remove();
        });
    }

    [Fact]
    public Task AddMenuItem_TwoItems_AddsThemInOrderAboveSeparatorAndExit()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var sut = CreateSut();

            // Act
            sut.AddMenuItem("Download model…", () => { });
            sut.AddMenuItem("Settings…", () => { });
            sut.UpdateMenuItems();

            // Assert
            sut.Menu.Items.Count.ShouldBe(4);
            ((NativeMenuItem) sut.Menu.Items[0]).Header.ShouldBe("Download model…");
            ((NativeMenuItem) sut.Menu.Items[1]).Header.ShouldBe("Settings…");
            sut.Menu.Items[2].ShouldBeOfType<NativeMenuItemSeparator>();
            ((NativeMenuItem) sut.Menu.Items[3]).Header.ShouldBe("Exit");
            sut.Remove();
        });
    }

    [Fact]
    public Task AddMenuItem_HeaderFunction_ShowsCurrentTextWhenMenuOpens()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var header = "A";
            var sut = CreateSut();
            sut.AddMenuItem(() => header, () => { });
            var item = (NativeMenuItem) sut.Menu.Items[0];

            // Act
            sut.UpdateMenuItems();
            var first = item.Header;
            header = "B";
            sut.UpdateMenuItems();
            var second = item.Header;

            // Assert
            first.ShouldBe("A");
            second.ShouldBe("B");
            sut.Remove();
        });
    }

    [Fact]
    public Task UpdateMenuItems_VisibilityFunctions_ShowOnlyVisibleItemsAndSeparatorWithThem()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var visible = false;
            var sut = CreateSut();
            sut.AddMenuItem("Cancel transcription", () => { }, () => visible);
            var item = (NativeMenuItem) sut.Menu.Items[0];
            var separator = (NativeMenuItem) sut.Menu.Items[1];

            // Act
            sut.UpdateMenuItems();
            var hidden = (item.IsVisible, separator.IsVisible);
            visible = true;
            sut.UpdateMenuItems();

            // Assert
            hidden.ShouldBe((false, false));
            item.IsVisible.ShouldBeTrue();
            separator.IsVisible.ShouldBeTrue();
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

    [Fact]
    public Task Remove_ThenOtherCalls_HaveNoEffect()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var taskbarMode = A.Fake<ITaskbarModeWatcher>();
            var sut = new TrayIconService(taskbarMode, new InlineUiDispatcher());
            sut.Show();
            sut.SetStatus(TrayStatus.Ready, "Pisum Transcribe – Ready (CPU)");
            var shown = sut.ShownIcon;

            // Act
            sut.Remove();
            sut.SetStatus(TrayStatus.Recording, "Pisum Transcribe – Recording…");
            sut.Show();
            taskbarMode.Changed += Raise.WithEmpty();
            sut.Remove();

            // Assert
            sut.IsShown.ShouldBeFalse();
            sut.ShownIcon.ShouldBeSameAs(shown);
            sut.ToolTip.ShouldBe("Pisum Transcribe – Ready (CPU)");
        });
    }

    private static TrayIconService CreateSut()
    {
        return new TrayIconService(A.Fake<ITaskbarModeWatcher>(), new InlineUiDispatcher());
    }

    /// <summary>
    /// A window with the type name of the tray menu's popup in Avalonia's Win32 backend.
    /// </summary>
    private sealed class TrayPopupRoot : Window;
}
