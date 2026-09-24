using Avalonia.Controls;
using Avalonia.VisualTree;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Pisum.Transcribe.Permissions;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SpeechModels;

namespace Pisum.Transcribe.Tests.Permissions;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class RelaunchServiceTests
{
    private const string BundlePath = "/Applications/Pisum Transcribe.app";

    private readonly IPermissions _permissions = A.Fake<IPermissions>();
    private readonly IModelStore _modelStore = A.Fake<IModelStore>();
    private readonly ISetupWindow _setupWindow = A.Fake<ISetupWindow>();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly PermissionsViewModel _viewModel;
    private PermissionState _accessibility = PermissionState.NotDetermined;
    private bool _isDownloading;
    private int _relaunchRequests;

    public RelaunchServiceTests()
    {
        A.CallTo(() => _permissions.GetState(Permission.Accessibility)).ReturnsLazily(() => _accessibility);
        A.CallTo(() => _permissions.GetState(Permission.Microphone)).Returns(PermissionState.Granted);
        A.CallTo(() => _permissions.GetState(Permission.PasteFromOtherApps)).Returns(PermissionState.Granted);
        A.CallTo(() => _permissions.GetNotificationsStateAsync()).Returns(PermissionState.Granted);
        A.CallTo(() => _modelStore.IsDownloading).ReturnsLazily(() => _isDownloading);
        _viewModel = new PermissionsViewModel(_permissions, new InlineUiDispatcher(), _timeProvider, _ => { });
    }

    [Fact]
    public async Task StartAsync_GrantedAtStart_NeverRelaunches()
    {
        // Arrange
        A.CallTo(() => _permissions.IsAccessibilityGrantedAtStart).Returns(true);
        _accessibility = PermissionState.Granted;
        var sut = CreateSut();

        // Act
        await sut.StartAsync(TestContext.Current.CancellationToken);
        _timeProvider.Advance(TimeSpan.FromMinutes(1));

        // Assert
        _relaunchRequests.ShouldBe(0);
        A.CallTo(() => _permissions.GetState(Permission.Accessibility)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Grant_WindowOpen_ShowsNoticeAndRelaunchesAfter3Seconds()
    {
        // Arrange
        A.CallTo(() => _setupWindow.IsOpen).Returns(true);
        var sut = CreateSut();
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        _accessibility = PermissionState.Granted;
        _timeProvider.Advance(RelaunchService.CheckInterval);
        var noteAfterGrant = _viewModel.Accessibility.Note;
        _timeProvider.Advance(RelaunchService.NoticeDuration - TimeSpan.FromTicks(1));
        var requestsBeforeNoticeEnds = _relaunchRequests;
        _timeProvider.Advance(TimeSpan.FromTicks(1));

        // Assert
        RelaunchService.NoticeDuration.ShouldBe(TimeSpan.FromSeconds(3));
        noteAfterGrant.ShouldBe("Pisum Transcribe restarts to turn on the hotkey");
        requestsBeforeNoticeEnds.ShouldBe(0);
        _relaunchRequests.ShouldBe(1);
    }

    [Fact]
    public async Task Grant_WindowClosed_RelaunchesWithin10Seconds()
    {
        // Arrange
        var sut = CreateSut();
        await sut.StartAsync(TestContext.Current.CancellationToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(1));

        // Act
        _accessibility = PermissionState.Granted;
        for (var elapsed = TimeSpan.Zero; elapsed < TimeSpan.FromSeconds(10) && _relaunchRequests == 0;
             elapsed += TimeSpan.FromMilliseconds(500))
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(500));
        }

        // Assert
        _relaunchRequests.ShouldBe(1);
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        _relaunchRequests.ShouldBe(1);
    }

    [Fact]
    public async Task Grant_DuringDownload_WaitsUntilDownloadStateChanged()
    {
        // Arrange
        A.CallTo(() => _setupWindow.IsOpen).Returns(true);
        _isDownloading = true;
        var sut = CreateSut();
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        _accessibility = PermissionState.Granted;
        _timeProvider.Advance(RelaunchService.CheckInterval);
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        var noteDuringDownload = _viewModel.Accessibility.Note;
        var requestsDuringDownload = _relaunchRequests;
        _isDownloading = false;
        _modelStore.DownloadStateChanged += Raise.WithEmpty();
        var noteAfterDownload = _viewModel.Accessibility.Note;
        _timeProvider.Advance(RelaunchService.NoticeDuration);

        // Assert
        noteDuringDownload.ShouldBe("Restarts when the download is finished");
        requestsDuringDownload.ShouldBe(0);
        noteAfterDownload.ShouldBe("Pisum Transcribe restarts to turn on the hotkey");
        _relaunchRequests.ShouldBe(1);
    }

    [Fact]
    public async Task Grant_DownloadStartsDuringNotice_WaitsUntilDownloadEndsThenShowsNoticeAgain()
    {
        // Arrange
        A.CallTo(() => _setupWindow.IsOpen).Returns(true);
        var sut = CreateSut();
        await sut.StartAsync(TestContext.Current.CancellationToken);
        _accessibility = PermissionState.Granted;
        _timeProvider.Advance(RelaunchService.CheckInterval);

        // Act
        _timeProvider.Advance(RelaunchService.NoticeDuration - TimeSpan.FromSeconds(1));
        _isDownloading = true;
        _modelStore.DownloadStateChanged += Raise.WithEmpty();
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        var noteDuringDownload = _viewModel.Accessibility.Note;
        var requestsDuringDownload = _relaunchRequests;
        _isDownloading = false;
        _modelStore.DownloadStateChanged += Raise.WithEmpty();
        var noteAfterDownload = _viewModel.Accessibility.Note;
        _timeProvider.Advance(RelaunchService.NoticeDuration - TimeSpan.FromTicks(1));
        var requestsBeforeNoticeEnds = _relaunchRequests;
        _timeProvider.Advance(TimeSpan.FromTicks(1));

        // Assert
        noteDuringDownload.ShouldBe("Restarts when the download is finished");
        requestsDuringDownload.ShouldBe(0);
        noteAfterDownload.ShouldBe("Pisum Transcribe restarts to turn on the hotkey");
        requestsBeforeNoticeEnds.ShouldBe(0);
        _relaunchRequests.ShouldBe(1);
    }

    [Fact]
    public async Task Grant_NoBundle_NeverRelaunches()
    {
        // Arrange
        var sut = CreateSut(null);
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        _accessibility = PermissionState.Granted;
        _timeProvider.Advance(TimeSpan.FromMinutes(1));

        // Assert
        _relaunchRequests.ShouldBe(0);
    }

    [Fact]
    public async Task Grant_WithoutPermissionRows_NeverRelaunches()
    {
        // Arrange: outside an app bundle the view model is null.
        var sut = new RelaunchService(_permissions, null, _modelStore, _setupWindow, new InlineUiDispatcher(),
            _timeProvider, NullLogger<RelaunchService>.Instance, BundlePath);
        sut.RelaunchRequested += (_, _) => _relaunchRequests++;
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        _accessibility = PermissionState.Granted;
        _timeProvider.Advance(TimeSpan.FromMinutes(1));

        // Assert
        _relaunchRequests.ShouldBe(0);
    }

    [Fact]
    public Task Grant_DuringDownloadWithWindowOpen_WindowShowsEachText()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            // Arrange
            var window = ShowSetupWindow();
            A.CallTo(() => _setupWindow.IsOpen).Returns(true);
            _isDownloading = true;
            var sut = CreateSut();
            await sut.StartAsync(TestContext.Current.CancellationToken);

            // Act
            _accessibility = PermissionState.Granted;
            _timeProvider.Advance(RelaunchService.CheckInterval);
            var textsDuringDownload = VisibleTexts(window);
            _isDownloading = false;
            _modelStore.DownloadStateChanged += Raise.WithEmpty();
            var textsBeforeRestart = VisibleTexts(window);

            // Assert
            textsDuringDownload.ShouldContain("Restarts when the download is finished");
            textsBeforeRestart.ShouldContain("Pisum Transcribe restarts to turn on the hotkey");
            textsBeforeRestart.ShouldNotContain("Restarts when the download is finished");
            window.Close();
        });
    }

    private RelaunchService CreateSut(string? bundlePath = BundlePath)
    {
        var sut = new RelaunchService(_permissions, _viewModel, _modelStore, _setupWindow, new InlineUiDispatcher(),
            _timeProvider, NullLogger<RelaunchService>.Instance, bundlePath);
        sut.RelaunchRequested += (_, _) => _relaunchRequests++;
        return sut;
    }

    private ModelSetupWindow ShowSetupWindow()
    {
        var settingsStore = A.Fake<ISettingsStore>();
        A.CallTo(() => settingsStore.Current).Returns(new AppSettings());
        var window = new ModelSetupWindow(new ModelSetupViewModel(_modelStore, settingsStore,
            A.Fake<IHostApplicationLifetime>(), _viewModel));
        window.Show();
        return window;
    }

    private static List<string?> VisibleTexts(Window window)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible)
            .Select(text => text.Text).ToList();
    }
}
