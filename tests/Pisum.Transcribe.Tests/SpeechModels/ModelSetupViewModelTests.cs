using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Pisum.Transcribe.Permissions;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SpeechModels;

namespace Pisum.Transcribe.Tests.SpeechModels;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class ModelSetupViewModelTests : IDisposable
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);

    private readonly IModelStore _modelStore = A.Fake<IModelStore>();
    private readonly ISettingsStore _settingsStore = A.Fake<ISettingsStore>();
    private readonly IHostApplicationLifetime _lifetime = A.Fake<IHostApplicationLifetime>();
    private readonly CancellationTokenSource _applicationStopping = new();
    private readonly TaskCompletionSource _install = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<CancellationToken> _installTokens = [];
    private readonly IPermissions _permissions = A.Fake<IPermissions>();
    private readonly Dictionary<Permission, PermissionState> _permissionStates = new()
    {
        [Permission.Accessibility] = PermissionState.NotDetermined,
        [Permission.Microphone] = PermissionState.NotDetermined,
        [Permission.PasteFromOtherApps] = PermissionState.NotDetermined,
    };

    private bool _accessibilityInEffect;
    private int _closeRequests;

    public ModelSetupViewModelTests()
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
        A.CallTo(() => _permissions.GetState(A<Permission>._))
            .ReturnsLazily((Permission permission) => _permissionStates[permission]);
        A.CallTo(() => _permissions.GetNotificationsStateAsync()).Returns(PermissionState.NotDetermined);
        A.CallTo(() => _permissions.IsAccessibilityInEffect).ReturnsLazily(() => _accessibilityInEffect);
    }

    public void Dispose()
    {
        _applicationStopping.Dispose();
    }

    [Fact]
    public void Constructor_SelectedModelInSettings_IsPreselected()
    {
        // Arrange
        A.CallTo(() => _settingsStore.Current)
            .Returns(new AppSettings {Model = new ModelSettings("canary-180m-flash-q8_0")});

        // Act
        var sut = CreateSut();

        // Assert
        sut.Models.Select(option => option.Model).ShouldBe(ModelCatalog.Models);
        sut.SelectedModel.Model.Id.ShouldBe("canary-180m-flash-q8_0");
        sut.SelectedModel.SizeText.ShouldBe("208 MB");
        sut.SelectedModel.LanguagesText.ShouldBe("English, French, German, Spanish");
    }

    [Fact]
    public async Task DownloadCommand_OtherModelSelected_SavesSelectionBeforeInstalling()
    {
        // Arrange
        var sut = CreateSut();
        SelectModel(sut, "canary-180m-flash-q8_0");

        // Act
        var download = sut.DownloadCommand.ExecuteAsync(null);
        var isDownloadingDuringInstall = sut.IsDownloading;
        _install.SetResult();
        await download.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        isDownloadingDuringInstall.ShouldBeTrue();
        A.CallTo(() => _settingsStore.SaveAsync(
                A<AppSettings>.That.Matches(settings => settings.Model.SelectedModelId == "canary-180m-flash-q8_0"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _modelStore.InstallAsync(
                    A<SpeechModel>.That.Matches(model => model.Id == "canary-180m-flash-q8_0"),
                    A<IProgress<DownloadProgress>>._, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly());
    }

    [Fact]
    public async Task DownloadCommand_InstallSucceeds_RequestsClose()
    {
        // Arrange
        var sut = CreateSut();
        _install.SetResult();

        // Act
        await sut.DownloadCommand.ExecuteAsync(null).WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        _closeRequests.ShouldBe(1);
        sut.IsDownloading.ShouldBeFalse();
        sut.HasError.ShouldBeFalse();
    }

    [Fact]
    public async Task CancelCommand_DuringDownload_StopsWithoutErrorAndKeepsNewSelection()
    {
        // Arrange
        var sut = CreateSut();
        SelectModel(sut, "canary-180m-flash-q8_0");
        var download = sut.DownloadCommand.ExecuteAsync(null);

        // Act
        sut.CancelCommand.Execute(null);
        await download.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        _installTokens.Single().IsCancellationRequested.ShouldBeTrue();
        sut.IsDownloading.ShouldBeFalse();
        sut.HasError.ShouldBeFalse();
        _closeRequests.ShouldBe(0);
        A.CallTo(() => _settingsStore.SaveAsync(
                A<AppSettings>.That.Matches(settings => settings.Model.SelectedModelId == "canary-180m-flash-q8_0"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task RetryCommand_AfterFailedDownload_ClearsErrorDownloadsAgainAndCloses()
    {
        // Arrange
        var sut = CreateSut();
        A.CallTo(() => _modelStore.InstallAsync(A<SpeechModel>._, A<IProgress<DownloadProgress>>._,
                A<CancellationToken>._))
            .ThrowsAsync(new HttpRequestException("Not found", null, HttpStatusCode.NotFound)).Once()
            .Then.Returns(Task.CompletedTask);
        await sut.DownloadCommand.ExecuteAsync(null).WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        var errorMessage = sut.ErrorMessage;
        var canRetry = sut.RetryCommand.CanExecute(null);

        // Act
        await sut.RetryCommand.ExecuteAsync(null).WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        errorMessage.ShouldBe("The download from huggingface.co failed with HTTP status 404 (NotFound). Try again.");
        canRetry.ShouldBeTrue();
        sut.HasError.ShouldBeFalse();
        sut.RetryCommand.CanExecute(null).ShouldBeFalse();
        _closeRequests.ShouldBe(1);
        A.CallTo(() => _modelStore.InstallAsync(A<SpeechModel>._, A<IProgress<DownloadProgress>>._,
                A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly();
    }

    [Fact]
    public async Task DownloadCommand_NotEnoughDiskSpace_ShowsRequiredSpace()
    {
        // Arrange
        var sut = CreateSut();
        A.CallTo(() => _modelStore.InstallAsync(A<SpeechModel>._, A<IProgress<DownloadProgress>>._,
                A<CancellationToken>._))
            .ThrowsAsync(new InsufficientDiskSpaceException(1_249_147_616, 838_860_800));

        // Act
        await sut.DownloadCommand.ExecuteAsync(null).WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        sut.ErrorMessage.ShouldBe(
            "Not enough disk space. The download needs 1.16 GB of free space, but only 800 MB is free.");
    }

    [Fact]
    public async Task DownloadCommand_ModelAlreadyDownloading_SaysSo()
    {
        // Arrange
        var sut = CreateSut();
        A.CallTo(() => _modelStore.InstallAsync(A<SpeechModel>._, A<IProgress<DownloadProgress>>._,
                A<CancellationToken>._))
            .ThrowsAsync(new ModelDownloadInProgressException("canary-1b-v2-q8_0"));

        // Act
        await sut.DownloadCommand.ExecuteAsync(null).WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        sut.ErrorMessage.ShouldBe("This model is already downloading.");
        _closeRequests.ShouldBe(0);
    }

    [Fact]
    public void ConfirmClose_NoDownload_ClosesWithoutAsking()
    {
        // Arrange
        var sut = CreateSut();
        var asked = false;

        // Act
        var mayClose = sut.ConfirmClose(() => asked = true);

        // Assert
        mayClose.ShouldBeTrue();
        asked.ShouldBeFalse();
    }

    [Fact]
    public async Task ConfirmClose_DownloadRunningAndConfirmed_CancelsDownloadAndCloses()
    {
        // Arrange
        var sut = CreateSut();
        var download = sut.DownloadCommand.ExecuteAsync(null);

        // Act
        var mayClose = sut.ConfirmClose(() => true);

        // Assert
        mayClose.ShouldBeTrue();
        _installTokens.Single().IsCancellationRequested.ShouldBeTrue();
        await download.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        sut.HasError.ShouldBeFalse();
    }

    [Fact]
    public async Task ConfirmClose_DownloadRunningAndDeclined_KeepsDownloadRunning()
    {
        // Arrange
        var sut = CreateSut();
        var download = sut.DownloadCommand.ExecuteAsync(null);

        // Act
        var mayClose = sut.ConfirmClose(() => false);

        // Assert
        mayClose.ShouldBeFalse();
        _installTokens.Single().IsCancellationRequested.ShouldBeFalse();
        sut.IsDownloading.ShouldBeTrue();
        _install.SetResult();
        await download.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConfirmClose_ApplicationStopping_ClosesWithoutAsking()
    {
        // Arrange
        var sut = CreateSut();
        var download = sut.DownloadCommand.ExecuteAsync(null);
        await _applicationStopping.CancelAsync();
        var asked = false;

        // Act
        var mayClose = sut.ConfirmClose(() => asked = true);

        // Assert
        mayClose.ShouldBeTrue();
        asked.ShouldBeFalse();
        _install.SetResult();
        await download.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConfirmClose_DownloadSucceedsWhileAsking_ClosesWithoutSeparateCloseRequest()
    {
        // Arrange
        var sut = CreateSut();
        var download = sut.DownloadCommand.ExecuteAsync(null);

        // Act
        var mayClose = sut.ConfirmClose(() =>
        {
            _install.SetResult();
            SpinWait.SpinUntil(() => download.IsCompleted, SignalTimeout);
            return false;
        });

        // Assert
        mayClose.ShouldBeTrue();
        _closeRequests.ShouldBe(0);
        await download.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DownloadCommand_RequiredPermissionsGrantedFirst_RequestsCloseAfterDownload()
    {
        // Arrange
        GrantRequiredPermissions();
        var sut = CreateSut(CreatePermissions());
        _install.SetResult();

        // Act
        await sut.DownloadCommand.ExecuteAsync(null).WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        _closeRequests.ShouldBe(1);
        sut.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public async Task DownloadCommand_AccessibilityMissing_KeepsWindowOpenUntilGranted()
    {
        // Arrange
        _permissionStates[Permission.Microphone] = PermissionState.Granted;
        var permissions = CreatePermissions();
        var sut = CreateSut(permissions);
        _install.SetResult();
        await sut.DownloadCommand.ExecuteAsync(null).WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
        var closeRequestsAfterDownload = _closeRequests;

        // Act
        _permissionStates[Permission.Accessibility] = PermissionState.Granted;
        await permissions.RefreshAsync();

        // Assert
        closeRequestsAfterDownload.ShouldBe(0);
        sut.IsModelInstalled.ShouldBeTrue();
        sut.IsComplete.ShouldBeFalse();
        _closeRequests.ShouldBe(0);
    }

    [Fact]
    public async Task RefreshAsync_ModelInstalledFirstThenMicrophoneGranted_RequestsClose()
    {
        // Arrange
        GrantAccessibilityAtStart();
        InstallSelectedModel();
        var permissions = CreatePermissions();
        var sut = CreateSut(permissions);
        var closeRequestsBefore = _closeRequests;

        // Act
        _permissionStates[Permission.Microphone] = PermissionState.Granted;
        await permissions.RefreshAsync();

        // Assert
        closeRequestsBefore.ShouldBe(0);
        _closeRequests.ShouldBe(1);
        sut.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public async Task RefreshAsync_OnlyOptionalPermissionsMissing_RequestsClose()
    {
        // Arrange: notifications and pasteboard access stay not determined.
        GrantAccessibilityAtStart();
        InstallSelectedModel();
        var permissions = CreatePermissions();
        CreateSut(permissions);

        // Act
        _permissionStates[Permission.Microphone] = PermissionState.Granted;
        await permissions.RefreshAsync();

        // Assert
        permissions.Notifications.State.ShouldBe(PermissionState.NotDetermined);
        permissions.PasteFromOtherApps.State.ShouldBe(PermissionState.NotDetermined);
        _closeRequests.ShouldBe(1);
    }

    [Fact]
    public async Task Dispose_WindowClosed_NoLongerFollowsPermissions()
    {
        // Arrange
        GrantAccessibilityAtStart();
        InstallSelectedModel();
        var permissions = CreatePermissions();
        var sut = CreateSut(permissions);

        // Act
        sut.Dispose();
        _permissionStates[Permission.Microphone] = PermissionState.Granted;
        await permissions.RefreshAsync();

        // Assert
        _closeRequests.ShouldBe(0);
    }

    [Fact]
    public void Constructor_WithoutPermissions_ShowsModelHeading()
    {
        // Act
        var sut = CreateSut();

        // Assert
        sut.HasPermissions.ShouldBeFalse();
        sut.Heading.ShouldBe("Download a speech model");
    }

    [Fact]
    public void Constructor_WithPermissionsAndModelInstalled_ShowsSetupHeadingAndInstalledModel()
    {
        // Arrange
        InstallSelectedModel();

        // Act
        var sut = CreateSut(CreatePermissions());

        // Assert
        sut.HasPermissions.ShouldBeTrue();
        sut.Heading.ShouldBe("Set up Pisum Transcribe");
        sut.IsModelInstalled.ShouldBeTrue();
        sut.InstalledModelText.ShouldBe("Speech model: Canary 1B v2 (Q8_0), installed");
        sut.DownloadCommand.CanExecute(null).ShouldBeFalse();
    }

    private ModelSetupViewModel CreateSut(PermissionsViewModel? permissions = null)
    {
        var sut = new ModelSetupViewModel(_modelStore, _settingsStore, _lifetime, permissions);
        sut.CloseRequested += (_, _) => _closeRequests++;
        return sut;
    }

    private PermissionsViewModel CreatePermissions()
    {
        return new PermissionsViewModel(_permissions, new InlineUiDispatcher(), new FakeTimeProvider(), _ => { });
    }

    private void GrantRequiredPermissions()
    {
        GrantAccessibilityAtStart();
        _permissionStates[Permission.Microphone] = PermissionState.Granted;
    }

    private void GrantAccessibilityAtStart()
    {
        _permissionStates[Permission.Accessibility] = PermissionState.Granted;
        _accessibilityInEffect = true;
    }

    private void InstallSelectedModel()
    {
        A.CallTo(() => _modelStore.IsInstalled(A<SpeechModel>._)).Returns(true);
    }

    private static void SelectModel(ModelSetupViewModel sut, string modelId)
    {
        sut.SelectedModel = sut.Models.Single(option => option.Model.Id == modelId);
    }
}
