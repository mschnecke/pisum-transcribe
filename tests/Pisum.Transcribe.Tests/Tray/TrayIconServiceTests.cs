using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using Pisum.Transcribe.Dictation;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.Tray;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class TrayIconServiceTests
{
    [Fact]
    public void IconFor_EachStatus_ReturnsItsIcon()
    {
        // Arrange
        var icons = new DictationIcons();

        // Act
        var iconsByStatus = Enum.GetValues<TrayStatus>()
            .ToDictionary(status => status, status => TrayIconService.IconFor(status, icons));

        // Assert
        iconsByStatus.Count.ShouldBe(4);
        iconsByStatus[TrayStatus.Ready].ShouldBeSameAs(icons.Ready);
        iconsByStatus[TrayStatus.Recording].ShouldBeSameAs(icons.Recording);
        iconsByStatus[TrayStatus.Transcribing].ShouldBeSameAs(icons.Transcribing);
        iconsByStatus[TrayStatus.Unavailable].ShouldBeSameAs(icons.Unavailable);
    }

    [Fact]
    public void SetStatus_StatusShownAgainAfterAnother_DoesNotThrow()
    {
        RunOnStaThread(() =>
        {
            // Arrange
            var sut = new TrayIconService(new DictationIcons());
            sut.SetStatus(TrayStatus.Ready, "Pisum Transcribe – Ready (CPU)");
            sut.SetStatus(TrayStatus.Recording, "Pisum Transcribe – Recording…");

            // Act
            var exception = Record.Exception(() => sut.SetStatus(TrayStatus.Ready, "Pisum Transcribe – Ready (CPU)"));

            // Assert
            exception.ShouldBeNull();
            sut.Remove();
        });
    }

    [Fact]
    public void Remove_AfterSetStatus_LeavesDictationIconsUsable()
    {
        RunOnStaThread(() =>
        {
            // Arrange
            var icons = new DictationIcons();
            var sut = new TrayIconService(icons);
            sut.SetStatus(TrayStatus.Ready, "Pisum Transcribe – Ready (CPU)");

            // Act
            sut.Remove();

            // Assert
            Should.NotThrow(() => icons.Ready.Handle);
        });
    }

    [Fact]
    public void AddMenuItem_HeaderFunction_ShowsCurrentTextWhenMenuOpens()
    {
        RunOnStaThread(() =>
        {
            // Arrange
            var header = "A";
            var sut = new TrayIconService(new DictationIcons());
            sut.AddMenuItem(() => header, () => { });
            var item = (MenuItem) sut.ContextMenu.Items[0];

            // Act
            sut.UpdateMenuItemVisibility();
            var first = item.Header;
            header = "B";
            sut.UpdateMenuItemVisibility();
            var second = item.Header;

            // Assert
            first.ShouldBe("A");
            second.ShouldBe("B");
            sut.Remove();
        });
    }

    /// <summary>
    /// Runs the test body on an STA thread, which the WPF tray icon needs.
    /// </summary>
    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }
}
