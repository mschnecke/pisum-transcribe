using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Pisum.Transcribe.Dialogs;

namespace Pisum.Transcribe.Tests.Dialogs;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class ConfirmDialogTests
{
    [Fact]
    public Task ShowAsync_YesClicked_ReturnsTrue()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            // Arrange
            var owner = ShowOwner();
            var answer = ConfirmDialog.ShowAsync(owner, "Delete the model?");
            var sut = OpenDialog(owner);

            // Act
            sut.YesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // Assert
            (await answer).ShouldBeTrue();
            sut.Title.ShouldBe(owner.Title);
            sut.Message.Text.ShouldBe("Delete the model?");
            owner.Close();
        });
    }

    [Fact]
    public Task ShowAsync_NoClicked_ReturnsFalse()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            // Arrange
            var owner = ShowOwner();
            var answer = ConfirmDialog.ShowAsync(owner, "Delete the model?");

            // Act
            OpenDialog(owner).NoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // Assert
            (await answer).ShouldBeFalse();
            owner.Close();
        });
    }

    [Fact]
    public Task ShowAsync_EscPressed_ReturnsFalse()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            // Arrange
            var owner = ShowOwner();
            var answer = ConfirmDialog.ShowAsync(owner, "Delete the model?");

            // Act
            OpenDialog(owner).KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

            // Assert
            (await answer).ShouldBeFalse();
            owner.Close();
        });
    }

    [Fact]
    public Task ShowAsync_EnterPressed_ReturnsFalseBecauseNoIsDefault()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            // Arrange
            var owner = ShowOwner();
            var answer = ConfirmDialog.ShowAsync(owner, "Delete the model?");
            var sut = OpenDialog(owner);

            // Act
            sut.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            // Assert
            (await answer).ShouldBeFalse();
            sut.NoButton.IsDefault.ShouldBeTrue();
            sut.YesButton.IsDefault.ShouldBeFalse();
            owner.Close();
        });
    }

    [Fact]
    public Task ShowAsync_OwnerClosed_ClosesDialogAndReturnsFalse()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            // Arrange
            var owner = ShowOwner();
            var answer = ConfirmDialog.ShowAsync(owner, "Delete the model?");
            var sut = OpenDialog(owner);

            // Act
            owner.Close();

            // Assert
            (await answer).ShouldBeFalse();
            sut.IsVisible.ShouldBeFalse();
        });
    }

    private static Window ShowOwner()
    {
        var owner = new Window {Title = "Pisum Transcribe"};
        owner.Show();
        return owner;
    }

    private static ConfirmDialog OpenDialog(Window owner)
    {
        return owner.OwnedWindows.OfType<ConfirmDialog>().Single();
    }
}
