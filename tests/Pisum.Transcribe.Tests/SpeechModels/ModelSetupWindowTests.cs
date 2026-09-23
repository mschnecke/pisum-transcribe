using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.Hosting;
using Pisum.Transcribe.Dialogs;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SpeechModels;

namespace Pisum.Transcribe.Tests.SpeechModels;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class ModelSetupWindowTests : IDisposable
{
    private readonly IModelStore _modelStore = A.Fake<IModelStore>();
    private readonly ISettingsStore _settingsStore = A.Fake<ISettingsStore>();
    private readonly IHostApplicationLifetime _lifetime = A.Fake<IHostApplicationLifetime>();
    private readonly CancellationTokenSource _applicationStopping = new();
    private readonly TaskCompletionSource _install = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<CancellationToken> _installTokens = [];

    public ModelSetupWindowTests()
    {
        A.CallTo(() => _settingsStore.Current).Returns(new AppSettings());
        A.CallTo(() => _lifetime.ApplicationStopping).Returns(_applicationStopping.Token);
        A.CallTo(() => _modelStore.InstallAsync(A<SpeechModel>._, A<IProgress<DownloadProgress>>._,
                A<CancellationToken>._))
            .ReturnsLazily((SpeechModel _, IProgress<DownloadProgress> _, CancellationToken token) =>
            {
                _installTokens.Add(token);
                return _install.Task.WaitAsync(token);
            });
    }

    public void Dispose()
    {
        _applicationStopping.Dispose();
    }

    [Fact]
    public Task Close_DownloadRunningAndNoChosen_KeepsWindowAndDownload()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var (sut, viewModel, closed) = ShowWindowWithDownload();

            // Act
            sut.Close();
            var dialog = sut.OwnedWindows.OfType<ConfirmDialog>().Single();
            dialog.NoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // Assert
            dialog.Message.Text.ShouldBe(ModelSetupWindow.ConfirmCloseMessage);
            closed().ShouldBeFalse();
            sut.IsVisible.ShouldBeTrue();
            viewModel.IsDownloading.ShouldBeTrue();
            _installTokens.Single().IsCancellationRequested.ShouldBeFalse();
            _applicationStopping.Cancel();
            sut.Close();
        });
    }

    [Fact]
    public Task Close_DownloadRunningAndYesChosen_CancelsDownloadAndCloses()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var (sut, _, closed) = ShowWindowWithDownload();

            // Act
            sut.Close();
            sut.OwnedWindows.OfType<ConfirmDialog>().Single().YesButton
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // Assert
            closed().ShouldBeTrue();
            _installTokens.Single().IsCancellationRequested.ShouldBeTrue();
        });
    }

    [Fact]
    public Task Close_NoDownload_ClosesWithoutAsking()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var sut = new ModelSetupWindow(CreateViewModel());
            var closed = false;
            sut.Closed += (_, _) => closed = true;
            sut.Show();

            // Act
            sut.Close();

            // Assert
            closed.ShouldBeTrue();
        });
    }

    [Fact]
    public Task Close_ApplicationStoppingDuringDownload_ClosesWithoutAsking()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var (sut, _, closed) = ShowWindowWithDownload();
            _applicationStopping.Cancel();

            // Act
            sut.Close();

            // Assert
            closed().ShouldBeTrue();
            sut.OwnedWindows.ShouldBeEmpty();
        });
    }

    [Fact]
    public Task Close_ApplicationStoppingWhileAsking_ClosesWindowAndConfirmation()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var (sut, _, closed) = ShowWindowWithDownload();
            sut.Close();
            var dialog = sut.OwnedWindows.OfType<ConfirmDialog>().Single();
            _applicationStopping.Cancel();

            // Act
            sut.Close();
            Dispatcher.UIThread.RunJobs();

            // Assert
            closed().ShouldBeTrue();
            dialog.IsVisible.ShouldBeFalse();
            sut.OwnedWindows.ShouldBeEmpty();
        });
    }

    private (ModelSetupWindow Window, ModelSetupViewModel ViewModel, Func<bool> Closed) ShowWindowWithDownload()
    {
        var viewModel = CreateViewModel();
        var window = new ModelSetupWindow(viewModel);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        _ = viewModel.DownloadCommand.ExecuteAsync(null);
        viewModel.IsDownloading.ShouldBeTrue();
        return (window, viewModel, () => closed);
    }

    private ModelSetupViewModel CreateViewModel()
    {
        return new ModelSetupViewModel(_modelStore, _settingsStore, _lifetime);
    }
}
