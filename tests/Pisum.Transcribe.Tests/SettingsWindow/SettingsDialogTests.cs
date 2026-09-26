using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Logging;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.Dialogs;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SettingsWindow;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Transcription;

namespace Pisum.Transcribe.Tests.SettingsWindow;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class SettingsDialogTests : IDisposable
{
    private const int GeneralSection = 4;

    private readonly CancellationTokenSource _applicationStopping = new();

    public void Dispose()
    {
        _applicationStopping.Dispose();
    }

    [Fact]
    public Task Constructor_EverySection_LaysOutWithoutBindingErrors()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var bindingErrors = new CollectingLogSink();
            var previousSink = Logger.Sink;
            Logger.Sink = bindingErrors;

            try
            {
                var viewModel = CreateViewModel(new AppSettings());

                // Act
                var sut = new SettingsDialog(viewModel);
                sut.Show();
                for (var section = 0; section < sut.Navigation.ItemCount; section++)
                {
                    sut.Navigation.SelectedIndex = section;
                    sut.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                }

                // Assert
                sut.Navigation.ItemCount.ShouldBe(5);
                bindingErrors.Messages.ShouldBeEmpty();
                sut.Close();
            }
            finally
            {
                Logger.Sink = previousSink;
            }
        });
    }

    [Fact]
    public Task Constructor_Always_CanBeMinimizedButNotResizedOrMaximized()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Act
            var sut = new SettingsDialog(CreateViewModel(new AppSettings()));

            // Assert
            sut.CanResize.ShouldBeFalse();
            sut.CanMaximize.ShouldBeFalse();
            sut.CanMinimize.ShouldBeTrue();
            sut.SizeToContent.ShouldBe(SizeToContent.WidthAndHeight);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Constructor_StartupRegistration_ShowsTheStartAtSignInOptionOnlyWithIt(bool hasRegistration)
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var viewModel = CreateViewModel(new FakeSettingsStore(new AppSettings()),
                hasRegistration ? A.Fake<IStartupRegistration>() : null, CreateModelStore());

            // Act
            var sut = new SettingsDialog(viewModel);
            sut.Show();
            sut.Navigation.SelectedIndex = GeneralSection;
            sut.UpdateLayout();

            // Assert
            sut.StartAtSignInRow.IsVisible.ShouldBe(hasRegistration);
            sut.Close();
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Constructor_RequiresApproval_ShowsTheApprovalHintOnlyThen(bool requiresApproval)
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var registration = A.Fake<IStartupRegistration>();
            A.CallTo(() => registration.RequiresApproval()).Returns(requiresApproval);
            var viewModel = CreateViewModel(new FakeSettingsStore(new AppSettings()), registration, CreateModelStore());

            // Act
            var sut = new SettingsDialog(viewModel);
            sut.Show();
            sut.Navigation.SelectedIndex = GeneralSection;
            sut.UpdateLayout();

            // Assert
            sut.StartAtSignInCheckBox.Content.ShouldBe(GeneralSectionViewModel.StartAtSignInLabel);
            sut.StartAtSignInCheckBox.IsChecked.ShouldBe(false);
            sut.ApprovalHint.IsVisible.ShouldBe(requiresApproval);
            sut.Close();
        });
    }

    [Fact]
    public Task SaveCommand_WithoutStartupRegistration_SavesTheSettings()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            // Arrange
            var settingsStore = new FakeSettingsStore(new AppSettings());
            var viewModel = CreateViewModel(settingsStore, null, CreateModelStore());
            var sut = new SettingsDialog(viewModel);
            sut.Show();
            var checkForUpdates = !viewModel.General.CheckForUpdates;
            viewModel.General.CheckForUpdates = checkForUpdates;

            // Act
            await viewModel.SaveCommand.ExecuteAsync(null);

            // Assert
            settingsStore.Saves.ShouldHaveSingleItem().Updates.CheckAutomatically.ShouldBe(checkForUpdates);
            viewModel.SaveError.ShouldBeNull();
            viewModel.HasChanges.ShouldBeFalse();
            sut.Close();
        });
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    public Task Constructor_EverySection_FitsWithoutScrolling(int runningDownloads, int failedDownloads)
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var modelStore = CreateModelStore();
            var installs = new Queue<Task>(Enumerable.Repeat(new TaskCompletionSource().Task, runningDownloads)
                .Concat(Enumerable.Repeat(
                    Task.FromException(new InsufficientDiskSpaceException(1_144_290_016, 52_428_800)),
                    failedDownloads)));
            A.CallTo(() => modelStore.InstallAsync(A<SpeechModel>._, A<IProgress<DownloadProgress>>._,
                    A<CancellationToken>._))
                .ReturnsLazily(() => installs.Dequeue());
            var viewModel = CreateViewModel(new AppSettings(), modelStore);
            var sut = new SettingsDialog(viewModel);
            foreach (var item in viewModel.Model.Items.Where(item => !item.IsInstalled)
                         .Take(runningDownloads + failedDownloads))
            {
                _ = item.DownloadCommand.ExecuteAsync(null);
            }

            // Act
            sut.Show();
            var scrollingSections = new List<object?>();
            for (var section = 0; section < sut.Navigation.ItemCount; section++)
            {
                sut.Navigation.SelectedIndex = section;
                Dispatcher.UIThread.RunJobs();
                sut.UpdateLayout();
                if (sut.SectionContent.Extent.Height > sut.SectionContent.Viewport.Height)
                {
                    scrollingSections.Add(((ListBoxItem) sut.Navigation.Items[section]!).Content);
                }
            }

            // Assert
            viewModel.Model.Items.Count(item => item.Download.IsDownloading).ShouldBe(runningDownloads);
            viewModel.Model.Items.Count(item => item.Download.HasError).ShouldBe(failedDownloads);
            scrollingSections.ShouldBeEmpty();
            _applicationStopping.Cancel();
            sut.Close();
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Constructor_SavedVoiceActivity_ShowsItAsTrimSilenceAndWritesClicksBack(bool enabled)
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var viewModel = CreateViewModel(new AppSettings {VoiceActivity = new VoiceActivitySettings(enabled)});

            // Act
            var sut = new SettingsDialog(viewModel);
            sut.Show();
            var checkBox = sut.GetLogicalDescendants().OfType<CheckBox>()
                .Single(candidate => Equals(candidate.Content, "Tri_m silence before transcription"));
            var shown = checkBox.IsChecked;
            checkBox.IsChecked = !enabled;

            // Assert
            shown.ShouldBe(enabled);
            viewModel.Dictation.TrimSilence.ShouldBe(!enabled);
            viewModel.HasChanges.ShouldBeTrue();
            sut.Close();
        });
    }

    [Fact]
    public Task Constructor_SavedGpuBackend_ShowsPlatformGpuOptionChecked()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var viewModel = CreateViewModel(new AppSettings
            {
                Transcription = new TranscriptionSettings(BackendPreference.Gpu),
            });
            var expected = OperatingSystem.IsWindows() ? "_Vulkan (GPU) only" : "_Metal (GPU) only";

            // Act
            var sut = new SettingsDialog(viewModel);
            sut.Show();
            var radioButton = sut.GetLogicalDescendants().OfType<RadioButton>()
                .Single(candidate => Equals(candidate.Content, expected));

            // Assert
            radioButton.IsChecked.ShouldBe(true);
            sut.Close();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DeleteCommand_ConfirmationAnswered_DeletesOnlyOnYes(bool yes)
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var modelStore = A.Fake<IModelStore>();
            A.CallTo(() => modelStore.IsInstalled(A<SpeechModel>._)).Returns(true);
            SettingsDialog? sut = null;
            var viewModel = CreateViewModel(new AppSettings(), modelStore, model => sut!.ConfirmDelete(model));
            sut = new SettingsDialog(viewModel);
            sut.Show();
            var item = viewModel.Model.Items.First(candidate => candidate.ShowsDelete);
            string? question = null;

            // Answered from inside the nested frame that waits for the dialog.
            Dispatcher.UIThread.Post(() =>
            {
                var dialog = sut.OwnedWindows.OfType<ConfirmDialog>().Single();
                question = dialog.Message.Text;
                (yes ? dialog.YesButton : dialog.NoButton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });

            // Act
            item.DeleteCommand.Execute(null);

            // Assert
            question.ShouldBe($"Delete {item.Model.DisplayName}? You can download it again later.");
            if (yes)
            {
                A.CallTo(() => modelStore.Delete(item.Model)).MustHaveHappenedOnceExactly();
            }
            else
            {
                A.CallTo(() => modelStore.Delete(A<SpeechModel>._)).MustNotHaveHappened();
            }

            sut.Close();
        });
    }

    [Fact]
    public Task Close_ApplicationStoppingDuringDownload_ClosesWithoutAsking()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            var (sut, closed) = ShowDialogWithDownload();
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
            var (sut, closed) = ShowDialogWithDownload();
            sut.Close();
            var dialog = sut.OwnedWindows.OfType<ConfirmDialog>().Single();
            dialog.Message.Text.ShouldBe(SettingsDialog.ConfirmCloseMessage);
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

    private (SettingsDialog Dialog, Func<bool> Closed) ShowDialogWithDownload()
    {
        var modelStore = CreateModelStore();
        A.CallTo(() => modelStore.InstallAsync(A<SpeechModel>._, A<IProgress<DownloadProgress>>._,
                A<CancellationToken>._))
            .ReturnsLazily((SpeechModel _, IProgress<DownloadProgress> _, CancellationToken token) =>
                Task.Delay(Timeout.Infinite, token));
        var viewModel = CreateViewModel(new AppSettings(), modelStore);
        var dialog = new SettingsDialog(viewModel);
        var closed = false;
        dialog.Closed += (_, _) => closed = true;
        dialog.Show();
        _ = viewModel.Model.Items.First(item => !item.IsInstalled).DownloadCommand.ExecuteAsync(null);
        viewModel.Model.IsDownloading.ShouldBeTrue();
        return (dialog, () => closed);
    }

    private static IModelStore CreateModelStore()
    {
        var modelStore = A.Fake<IModelStore>();
        A.CallTo(() => modelStore.IsInstalled(A<SpeechModel>._))
            .ReturnsLazily((SpeechModel model) => model.Id == ModelCatalog.DefaultModelId);
        return modelStore;
    }

    private SettingsViewModel CreateViewModel(AppSettings settings)
    {
        return CreateViewModel(settings, CreateModelStore());
    }

    private SettingsViewModel CreateViewModel(AppSettings settings,
                                              IModelStore modelStore,
                                              Func<SpeechModel, bool>? confirmDelete = null)
    {
        return CreateViewModel(new FakeSettingsStore(settings), A.Fake<IStartupRegistration>(), modelStore,
            confirmDelete);
    }

    private SettingsViewModel CreateViewModel(ISettingsStore settingsStore,
                                              IStartupRegistration? startupRegistration,
                                              IModelStore modelStore,
                                              Func<SpeechModel, bool>? confirmDelete = null)
    {
        var transcriber = A.Fake<ITranscriber>();
        A.CallTo(() => transcriber.Status).Returns(TranscriberStatus.Ready);
        A.CallTo(() => transcriber.ActiveBackend).Returns("Vulkan");
        var lifetime = A.Fake<IHostApplicationLifetime>();
        A.CallTo(() => lifetime.ApplicationStopping).Returns(_applicationStopping.Token);
        return new SettingsViewModel(settingsStore, startupRegistration, modelStore, transcriber, A.Fake<IPushToTalkHotkey>(), lifetime, confirmDelete ?? (_ => false),
            NullLogger<SettingsViewModel>.Instance, new InlineUiDispatcher());
    }

    private sealed class CollectingLogSink : ILogSink
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return _messages.ToList();
                }
            }
        }

        public bool IsEnabled(LogEventLevel level, string area)
        {
            return level >= LogEventLevel.Warning && area == LogArea.Binding;
        }

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        {
            Log(level, area, source, messageTemplate, []);
        }

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate,
                        params object?[] propertyValues)
        {
            if (IsEnabled(level, area))
            {
                lock (_messages)
                {
                    _messages.Add($"{messageTemplate} {string.Join(", ", propertyValues)}");
                }
            }
        }
    }
}
