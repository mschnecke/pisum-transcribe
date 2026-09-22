using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.Tray;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class TrayIconServiceTests
{
    [Fact]
    public void SetStatus_IconShownAgainAfterAnother_DoesNotThrow()
    {
        RunOnStaThread(() =>
        {
            // Arrange
            using var ready = (Icon) SystemIcons.Application.Clone();
            using var recording = (Icon) SystemIcons.Information.Clone();
            var sut = new TrayIconService();
            sut.SetStatus(ready, "Pisum Transcribe – Ready (CPU)");
            sut.SetStatus(recording, "Pisum Transcribe – Recording…");

            // Act
            var exception = Record.Exception(() => sut.SetStatus(ready, "Pisum Transcribe – Ready (CPU)"));

            // Assert
            exception.ShouldBeNull();
            sut.Remove();
        });
    }

    [Fact]
    public void Remove_AfterSetStatus_LeavesCallerIconUsable()
    {
        RunOnStaThread(() =>
        {
            // Arrange
            using var ready = (Icon) SystemIcons.Application.Clone();
            var sut = new TrayIconService();
            sut.SetStatus(ready, "Pisum Transcribe – Ready (CPU)");

            // Act
            sut.Remove();

            // Assert
            Should.NotThrow(() => ready.Handle);
        });
    }

    [Fact]
    public void AddMenuItem_HeaderFunction_ShowsCurrentTextWhenMenuOpens()
    {
        RunOnStaThread(() =>
        {
            // Arrange
            var header = "A";
            var sut = new TrayIconService();
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
