using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SettingsWindow;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Transcription;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.SettingsWindow;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class SettingsWindowServiceTests
{
    private readonly ITrayIconService _trayIcon = A.Fake<ITrayIconService>();

    [Fact]
    public Task Clicked_WindowClosed_OpensSettingsWindow()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            // Arrange
            var sut = CreateSut();
            await sut.StartAsync(TestContext.Current.CancellationToken);

            // Act
            _trayIcon.Clicked += Raise.WithEmpty();
            Dispatcher.UIThread.RunJobs();

            // Assert
            sut.Dialog.ShouldNotBeNull();
            sut.Dialog.IsVisible.ShouldBeTrue();
            sut.Dialog.IsActive.ShouldBeTrue();
            sut.Dialog.Close();
        });
    }

    [Fact]
    public Task Clicked_TwiceWithWindowMinimized_KeepsOneWindowAndRestoresAndActivatesIt()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            // Arrange
            var sut = CreateSut();
            await sut.StartAsync(TestContext.Current.CancellationToken);
            _trayIcon.Clicked += Raise.WithEmpty();
            var first = sut.Dialog!;
            first.WindowState = WindowState.Minimized;

            // Act
            _trayIcon.Clicked += Raise.WithEmpty();
            Dispatcher.UIThread.RunJobs();

            // Assert
            sut.Dialog.ShouldBeSameAs(first);
            first.WindowState.ShouldBe(WindowState.Normal);
            first.IsActive.ShouldBeTrue();
            first.Close();
        });
    }

    [Fact]
    public Task StopAsync_ThenClicked_OpensNoWindow()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            // Arrange
            var sut = CreateSut();
            await sut.StartAsync(TestContext.Current.CancellationToken);
            await sut.StopAsync(TestContext.Current.CancellationToken);

            // Act
            _trayIcon.Clicked += Raise.WithEmpty();

            // Assert
            sut.Dialog.ShouldBeNull();
        });
    }

    private SettingsWindowService CreateSut()
    {
        var modelStore = A.Fake<IModelStore>();
        A.CallTo(() => modelStore.IsInstalled(A<SpeechModel>._)).Returns(true);
        var transcriber = A.Fake<ITranscriber>();
        A.CallTo(() => transcriber.Status).Returns(TranscriberStatus.Ready);
        return new SettingsWindowService(_trayIcon, new InlineUiDispatcher(), new FakeSettingsStore(new AppSettings()),
            A.Fake<IStartupRegistration>(), modelStore, transcriber, A.Fake<IPushToTalkHotkey>(),
            A.Fake<IHostApplicationLifetime>(), NullLogger<SettingsViewModel>.Instance);
    }
}
