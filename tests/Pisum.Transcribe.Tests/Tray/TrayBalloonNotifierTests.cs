using System.Runtime.ExceptionServices;
using Pisum.Transcribe.Dictation;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.Tray;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class TrayBalloonNotifierTests
{
    [Fact]
    public void Show_FromAnyThread_QueuesOnUiDispatcher()
    {
        RunOnStaThread(() =>
        {
            // Arrange
            var uiDispatcher = A.Fake<IUiDispatcher>();
            var trayIcon = new TrayIconService(new DictationIcons());
            var sut = new TrayBalloonNotifier(uiDispatcher, trayIcon);
            var caller = new Thread(() => sut.Show("Title", "Message"));

            // Act
            caller.Start();
            caller.Join();

            // Assert
            A.CallTo(() => uiDispatcher.InvokeAsync(A<Action>._)).MustHaveHappenedOnceExactly();
            trayIcon.Remove();
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
