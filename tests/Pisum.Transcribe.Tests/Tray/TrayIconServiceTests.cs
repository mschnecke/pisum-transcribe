using System.Drawing;
using System.Runtime.ExceptionServices;
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
