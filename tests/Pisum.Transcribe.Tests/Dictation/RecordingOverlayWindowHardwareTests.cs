using Avalonia;
using Avalonia.Threading;
using Pisum.Transcribe.Dictation;
using Pisum.Transcribe.Tests.TextInsertion;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Pisum.Transcribe.Tests.Dictation;

/// <summary>
/// Shows the overlay on the real desktop, with Avalonia's Win32 platform. Avalonia runs one platform per process, so run
/// these tests without the headless ones, for example with <c>--filter-trait "Category=Hardware" --explicit on</c>.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Hardware)]
[Collection(DesktopCollection.Name)]
public sealed class RecordingOverlayWindowHardwareTests
{
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(300);

    [Fact(Explicit = true)]
    public async Task ShowStarting_ShownHiddenAndShownAgain_SetsExtendedStylesAndKeepsForeground()
    {
        // Arrange: the test window first, while the process may still take the foreground.
        Assert.SkipWhen(Application.Current is not null && !Win32Ui.IsStarted,
            "The headless platform already runs in this process. Run the hardware tests on their own.");
        using var target = TestWindow.Open();
        TextInsertionTestAssertions.ShouldBeForeground(target);
        Win32Ui.Start();
        var sut = await Win32Ui.InvokeAsync(() => new RecordingOverlayWindow());

        for (var round = 1; round <= 2; round++)
        {
            // Act
            await Win32Ui.InvokeAsync(() => ((IRecordingOverlay) sut).ShowStarting(target.Handle));
            await Task.Delay(SettleTime, TestContext.Current.CancellationToken);
            var styles = await Win32Ui.InvokeAsync(() => ReadExtendedStyles(sut));
            var foreground = (nint) PInvoke.GetForegroundWindow();
            await Win32Ui.InvokeAsync(() => ((IRecordingOverlay) sut).Hide());

            // Assert
            (styles & RecordingOverlayWindow.ExtendedStyles).ShouldBe(RecordingOverlayWindow.ExtendedStyles,
                $"Show {round} lost extended styles.");
            foreground.ShouldBe(target.Handle, $"Show {round} took the foreground.");
        }

        await Win32Ui.InvokeAsync(sut.Close);
    }

    private static WINDOW_EX_STYLE ReadExtendedStyles(RecordingOverlayWindow window)
    {
        var handle = (HWND) window.TryGetPlatformHandle()!.Handle;
        return (WINDOW_EX_STYLE) (uint) PInvoke.GetWindowLongPtr(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
    }

    /// <summary>
    /// Avalonia's Win32 platform on a UI thread of its own, started once per process.
    /// </summary>
    private static class Win32Ui
    {
        private static readonly Lock StartLock = new();

        public static bool IsStarted { get; private set; }

        public static void Start()
        {
            lock (StartLock)
            {
                if (IsStarted)
                {
                    return;
                }

                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var thread = new Thread(() =>
                {
                    AppBuilder.Configure<Application>().UseWin32().UseSkia().UseHarfBuzz().SetupWithoutStarting();
                    ready.SetResult();
                    Dispatcher.UIThread.MainLoop(CancellationToken.None);
                }) {IsBackground = true, Name = "Avalonia Win32 UI"};
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                ready.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                IsStarted = true;
            }
        }

        public static Task InvokeAsync(Action action)
        {
            return Dispatcher.UIThread.InvokeAsync(action).GetTask();
        }

        public static Task<T> InvokeAsync<T>(Func<T> action)
        {
            return Dispatcher.UIThread.InvokeAsync(action).GetTask();
        }
    }
}
