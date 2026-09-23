using Avalonia.Controls;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.Tray;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class TrayIconServiceTests
{
    [Fact]
    public Task Constructor_Always_ShowsInitialIcon()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var icons = new FakeTrayIconSet();

            // Act
            var sut = new TrayIconService(icons, new InlineUiDispatcher());

            // Assert
            sut.ShownIcon.ShouldBeSameAs(icons.Initial);
            sut.IsTemplateIcon.ShouldBeFalse();
            sut.Remove();
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
            var icons = new FakeTrayIconSet();
            var sut = new TrayIconService(icons, new InlineUiDispatcher());

            // Act
            sut.SetStatus(TrayStatus.Recording, "Pisum Transcribe – Recording…");

            // Assert
            sut.ShownIcon.ShouldBeSameAs(icons.For(TrayStatus.Recording));
            sut.IsTemplateIcon.ShouldBeFalse();
            sut.ToolTip.ShouldBe("Pisum Transcribe – Recording…");
            sut.Remove();
        });
    }

    [Fact]
    public Task SetStatus_ReadyThenRecording_SetsTemplateFlagWithEachIcon()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var icons = new FakeTrayIconSet();
            var sut = new TrayIconService(icons, new InlineUiDispatcher());

            // Act
            sut.SetStatus(TrayStatus.Ready, "Pisum Transcribe – Ready (CPU)");
            var readyIsTemplate = sut.IsTemplateIcon;
            sut.SetStatus(TrayStatus.Recording, "Pisum Transcribe – Recording…");

            // Assert
            readyIsTemplate.ShouldBeTrue();
            sut.IsTemplateIcon.ShouldBeFalse();
            sut.Remove();
        });
    }

    [Fact]
    public Task IconsChanged_WhileReady_AppliesNewIcon()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var icons = new FakeTrayIconSet();
            var sut = new TrayIconService(icons, new InlineUiDispatcher());
            sut.SetStatus(TrayStatus.Ready, "Pisum Transcribe – Ready (CPU)");

            // Act
            var replaced = icons.Replace(TrayStatus.Ready);

            // Assert
            sut.ShownIcon.ShouldBeSameAs(replaced);
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
            ((NativeMenuItem) sut.Menu.Items[3]).Header.ShouldBe(TrayIconService.ExitHeader);
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
    public Task Remove_ThenOtherCalls_HaveNoEffect()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var icons = new FakeTrayIconSet();
            var sut = new TrayIconService(icons, new InlineUiDispatcher());
            sut.Show();
            sut.SetStatus(TrayStatus.Ready, "Pisum Transcribe – Ready (CPU)");
            var shown = sut.ShownIcon;

            // Act
            sut.Remove();
            sut.SetStatus(TrayStatus.Recording, "Pisum Transcribe – Recording…");
            sut.Show();
            icons.Replace(TrayStatus.Ready);
            sut.Remove();

            // Assert
            sut.IsShown.ShouldBeFalse();
            sut.ShownIcon.ShouldBeSameAs(shown);
            sut.ToolTip.ShouldBe("Pisum Transcribe – Ready (CPU)");
        });
    }

    private static TrayIconService CreateSut()
    {
        return new TrayIconService(new FakeTrayIconSet(), new InlineUiDispatcher());
    }
}
