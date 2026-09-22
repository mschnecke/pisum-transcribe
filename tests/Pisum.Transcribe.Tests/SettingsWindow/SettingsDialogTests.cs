using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SettingsWindow;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Transcription;

namespace Pisum.Transcribe.Tests.SettingsWindow;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class SettingsDialogTests
{
    [Fact]
    public void Constructor_EverySection_LaysOutWithoutBindingErrors()
    {
        RunOnStaThread(() =>
        {
            // Arrange
            var bindingErrors = new CollectingTraceListener();
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);

            try
            {
                var viewModel = CreateViewModel(new AppSettings());

                // Act
                var sut = new SettingsDialog(viewModel);
                var content = (UIElement) sut.Content;
                for (var section = 0; section < sut.Navigation.Items.Count; section++)
                {
                    sut.Navigation.SelectedIndex = section;
                    content.Measure(new Size(720, 600));
                    content.Arrange(new Rect(0, 0, 720, 600));
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                }

                // Assert
                sut.Navigation.Items.Count.ShouldBe(5);
                bindingErrors.Messages.ShouldBeEmpty();
            }
            finally
            {
                PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingErrors);
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
    }

    [Fact]
    public void Constructor_Always_CanBeMinimizedButNotResizedOrMaximized()
    {
        RunOnStaThread(() =>
        {
            try
            {
                // Act
                var sut = new SettingsDialog(CreateViewModel(new AppSettings()));

                // Assert
                sut.ResizeMode.ShouldBe(ResizeMode.CanMinimize);
                sut.SizeToContent.ShouldBe(SizeToContent.WidthAndHeight);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    public void Constructor_EverySection_FitsWithoutScrolling(int runningDownloads, int failedDownloads)
    {
        RunOnStaThread(() =>
        {
            try
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
                var content = (UIElement) sut.Content;
                var scrollingSections = new List<object>();
                for (var section = 0; section < sut.Navigation.Items.Count; section++)
                {
                    sut.Navigation.SelectedIndex = section;
                    content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    content.Arrange(new Rect(content.DesiredSize));
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    content.UpdateLayout();
                    if (sut.SectionContent.ScrollableHeight > 0)
                    {
                        scrollingSections.Add(((ListBoxItem) sut.Navigation.Items[section]).Content);
                    }
                }

                // Assert
                viewModel.Model.Items.Count(item => item.Download.IsDownloading).ShouldBe(runningDownloads);
                viewModel.Model.Items.Count(item => item.Download.HasError).ShouldBe(failedDownloads);
                scrollingSections.ShouldBeEmpty();
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Constructor_SavedVoiceActivity_ShowsItAsTrimSilenceAndWritesClicksBack(bool enabled)
    {
        RunOnStaThread(() =>
        {
            try
            {
                // Arrange
                var viewModel = CreateViewModel(new AppSettings {VoiceActivity = new VoiceActivitySettings(enabled)});

                // Act
                var sut = new SettingsDialog(viewModel);
                var content = (UIElement) sut.Content;
                content.Measure(new Size(720, 600));
                content.Arrange(new Rect(0, 0, 720, 600));
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                var checkBox = FindLogical<CheckBox>(sut)
                    .Single(candidate => Equals(candidate.Content, "Tri_m silence before transcription"));
                var shown = checkBox.IsChecked;
                checkBox.IsChecked = !enabled;

                // Assert
                shown.ShouldBe(enabled);
                viewModel.Dictation.TrimSilence.ShouldBe(!enabled);
                viewModel.HasChanges.ShouldBeTrue();
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
    }

    private static IModelStore CreateModelStore()
    {
        var modelStore = A.Fake<IModelStore>();
        A.CallTo(() => modelStore.IsInstalled(A<SpeechModel>._))
            .ReturnsLazily((SpeechModel model) => model.Id == ModelCatalog.DefaultModelId);
        return modelStore;
    }

    private static SettingsViewModel CreateViewModel(AppSettings settings)
    {
        return CreateViewModel(settings, CreateModelStore());
    }

    private static SettingsViewModel CreateViewModel(AppSettings settings, IModelStore modelStore)
    {
        var transcriber = A.Fake<ITranscriber>();
        A.CallTo(() => transcriber.Status).Returns(TranscriberStatus.Ready);
        A.CallTo(() => transcriber.ActiveBackend).Returns("Vulkan");
        return new SettingsViewModel(new FakeSettingsStore(settings), A.Fake<IStartupRegistration>(), modelStore,
            transcriber, A.Fake<IPushToTalkHotkey>(), A.Fake<IHostApplicationLifetime>(), _ => false,
            NullLogger<SettingsViewModel>.Instance, new InlineUiDispatcher());
    }

    private static IEnumerable<T> FindLogical<T>(DependencyObject parent)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindLogical<T>(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Runs the test body on an STA thread, which WPF windows need.
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

    private sealed class CollectingTraceListener : TraceListener
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages => _messages;

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (message is not null)
            {
                _messages.Add(message);
            }
        }
    }
}
