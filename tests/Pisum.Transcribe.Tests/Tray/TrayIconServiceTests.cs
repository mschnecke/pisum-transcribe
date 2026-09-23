using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Tray;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Pisum.Transcribe.Tests.Tray;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class TrayIconServiceTests
{
    [Fact]
    public void IconFor_EachStatusAndMode_ReturnsMatchingIcon()
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
    }

    [Fact]
    public void LoadIcons_Always_LoadsSmallIconSize()
    {
        // Arrange
        var smallIconSize = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON);

        // Act
        var icons = TrayIconService.LoadIcons();

        // Assert
        Icon[] all =
        [
            icons.ReadyLight, icons.ReadyDark, icons.UnavailableLight, icons.UnavailableDark, icons.Recording,
            icons.Transcribing,
        ];
        all.ShouldAllBe(icon => icon.Width == smallIconSize && icon.Height == smallIconSize);
    }

    [Fact]
    public void TaskbarModeChanged_WhileReady_AppliesIconForNewMode()
    {
        RunOnStaThread(() =>
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
    public void SetStatus_StatusShownAgainAfterAnother_DoesNotThrow()
    {
        RunOnStaThread(() =>
        {
            // Arrange
            var sut = CreateSut();
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
    public void Remove_AfterSetStatus_LeavesLoadedIconsUsable()
    {
        RunOnStaThread(() =>
        {
            // Arrange
            var sut = CreateSut();
            sut.SetStatus(TrayStatus.Ready, "Pisum Transcribe – Ready (CPU)");
            var shown = sut.ShownIcon!;

            // Act
            sut.Remove();

            // Assert
            Should.NotThrow(() => shown.Handle);
        });
    }

    [Fact]
    public void AddMenuItem_HeaderFunction_ShowsCurrentTextWhenMenuOpens()
    {
        RunOnStaThread(() =>
        {
            // Arrange
            var header = "A";
            var sut = CreateSut();
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

    private static TrayIconService CreateSut()
    {
        return new TrayIconService(A.Fake<ITaskbarModeWatcher>(), new InlineUiDispatcher());
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
