using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SettingsWindow;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Transcription;

namespace Pisum.Transcribe.Tests.SettingsWindow;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class ModelSectionViewModelTests : IDisposable
{
    private const string DefaultModelId = ModelCatalog.DefaultModelId;
    private const string MediumModelId = "canary-1b-v2-q4_k_m";
    private const string SmallModelId = "canary-180m-flash-q8_0";

    private readonly IModelStore _modelStore = A.Fake<IModelStore>();
    private readonly ITranscriber _transcriber = A.Fake<ITranscriber>();
    private readonly CancellationTokenSource _applicationStopping = new();
    private readonly HashSet<string> _installed = [DefaultModelId, MediumModelId];
    private readonly TaskCompletionSource _install = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<CancellationToken> _installTokens = [];
    private readonly List<SpeechModel> _deleteConfirmations = [];
    private bool _confirmDelete = true;

    public ModelSectionViewModelTests()
    {
        A.CallTo(() => _modelStore.IsInstalled(A<SpeechModel>._))
            .ReturnsLazily((SpeechModel model) => _installed.Contains(model.Id));
        A.CallTo(() => _modelStore.InstallAsync(A<SpeechModel>._, A<IProgress<DownloadProgress>>._,
                A<CancellationToken>._))
            .ReturnsLazily((SpeechModel model, IProgress<DownloadProgress> _, CancellationToken token) =>
            {
                _installTokens.Add(token);
                return _install.Task.WaitAsync(token).ContinueWith(install =>
                {
                    install.GetAwaiter().GetResult();
                    _installed.Add(model.Id);
                }, TaskScheduler.Default);
            });
        A.CallTo(() => _transcriber.Status).Returns(TranscriberStatus.Ready);
        A.CallTo(() => _transcriber.ActiveBackend).Returns("Vulkan");
    }

    public void Dispose()
    {
        _applicationStopping.Dispose();
    }

    [Fact]
    public void Items_SavedModelInstalled_MarksItActiveAndSelected()
    {
        // Act
        var sut = CreateSut();

        // Assert
        var item = Item(sut, DefaultModelId);
        item.IsActive.ShouldBeTrue();
        item.IsSelected.ShouldBeTrue();
        sut.Items.Select(candidate => candidate.IsInstalled).ShouldBe([true, true, false]);
        sut.EngineStatusText.ShouldBe("Ready on Vulkan");
    }

    [Fact]
    public void Delete_SavedActiveModel_IsUnavailable()
    {
        // Arrange
        var sut = CreateSut();
        sut.SelectModel(MediumModelId);

        // Act
        var item = Item(sut, DefaultModelId);

        // Assert
        item.IsSelected.ShouldBeFalse();
        item.ShowsDelete.ShouldBeFalse();
        item.DeleteCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void Delete_ModelSelectedInWindow_IsUnavailable()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        sut.SelectModel(MediumModelId);

        // Assert
        var item = Item(sut, MediumModelId);
        item.ShowsDelete.ShouldBeFalse();
        item.DeleteCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void Delete_WhileEngineLoads_IsUnavailableForAnyModel()
    {
        // Arrange
        var sut = CreateSut();
        var item = Item(sut, MediumModelId);
        var canDeleteWhileReady = item.DeleteCommand.CanExecute(null);

        // Act
        RaiseEngineStatus(TranscriberStatus.Loading);

        // Assert
        canDeleteWhileReady.ShouldBeTrue();
        item.ShowsDelete.ShouldBeTrue();
        item.DeleteCommand.CanExecute(null).ShouldBeFalse();
        sut.EngineStatusText.ShouldBe(ModelSectionViewModel.LoadingText);
    }

    [Fact]
    public void Delete_InactiveModelConfirmed_RemovesItAndShowsNotInstalled()
    {
        // Arrange
        var sut = CreateSut();
        var item = Item(sut, MediumModelId);
        A.CallTo(() => _modelStore.Delete(item.Model)).Invokes(() => _installed.Remove(MediumModelId));

        // Act
        item.DeleteCommand.Execute(null);

        // Assert
        _deleteConfirmations.ShouldBe([item.Model]);
        item.IsInstalled.ShouldBeFalse();
        item.DeleteError.ShouldBeNull();
        item.DownloadCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void Delete_Declined_KeepsModel()
    {
        // Arrange
        _confirmDelete = false;
        var sut = CreateSut();
        var item = Item(sut, MediumModelId);

        // Act
        item.DeleteCommand.Execute(null);

        // Assert
        A.CallTo(() => _modelStore.Delete(A<SpeechModel>._)).MustNotHaveHappened();
        item.IsInstalled.ShouldBeTrue();
    }

    [Fact]
    public void Delete_FileInUse_ShowsMessageAndStaysInstalled()
    {
        // Arrange
        var sut = CreateSut();
        var item = Item(sut, MediumModelId);
        A.CallTo(() => _modelStore.Delete(item.Model)).Throws(new IOException("The file is in use."));

        // Act
        item.DeleteCommand.Execute(null);

        // Assert
        item.DeleteError.ShouldBe(ModelItemViewModel.DeleteFailedMessage);
        item.IsInstalled.ShouldBeTrue();
    }

    [Fact]
    public async Task Download_Succeeds_ShowsInstalledAndSelectable()
    {
        // Arrange
        var sut = CreateSut();
        var item = Item(sut, SmallModelId);
        var selectableBefore = sut.SelectModel(SmallModelId);
        var download = item.DownloadCommand.ExecuteAsync(null);
        var isDownloading = item.Download.IsDownloading;

        // Act
        _install.SetResult();
        await download;

        // Assert
        selectableBefore.ShouldBeFalse();
        isDownloading.ShouldBeTrue();
        item.IsInstalled.ShouldBeTrue();
        sut.SelectModel(SmallModelId).ShouldBeTrue();
        sut.SelectedModelId.ShouldBe(SmallModelId);
    }

    [Fact]
    public async Task Download_AlreadyDownloadingElsewhere_SaysSo()
    {
        // Arrange
        var sut = CreateSut();
        var item = Item(sut, SmallModelId);
        A.CallTo(() => _modelStore.InstallAsync(item.Model, A<IProgress<DownloadProgress>>._,
                A<CancellationToken>._))
            .ThrowsAsync(new ModelDownloadInProgressException(SmallModelId));

        // Act
        await item.DownloadCommand.ExecuteAsync(null);

        // Assert
        item.Download.ErrorMessage.ShouldBe("This model is already downloading.");
        item.IsInstalled.ShouldBeFalse();
    }

    [Fact]
    public async Task ConfirmClose_DownloadRunningAndConfirmed_CancelsDownload()
    {
        // Arrange
        var sut = CreateSut();
        var download = Item(sut, SmallModelId).DownloadCommand.ExecuteAsync(null);
        var asked = false;

        // Act
        var mayClose = sut.ConfirmClose(() => asked = true);

        // Assert
        await download;
        mayClose.ShouldBeTrue();
        asked.ShouldBeTrue();
        _installTokens.Single().IsCancellationRequested.ShouldBeTrue();
        Item(sut, SmallModelId).Download.HasError.ShouldBeFalse();
    }

    [Fact]
    public async Task ConfirmClose_DownloadRunningAndDeclined_KeepsDownloading()
    {
        // Arrange
        var sut = CreateSut();
        var download = Item(sut, SmallModelId).DownloadCommand.ExecuteAsync(null);

        // Act
        var mayClose = sut.ConfirmClose(() => false);

        // Assert
        mayClose.ShouldBeFalse();
        _installTokens.Single().IsCancellationRequested.ShouldBeFalse();
        _install.SetResult();
        await download;
    }

    [Fact]
    public void ConfirmClose_NoDownload_ClosesWithoutAsking()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var mayClose = sut.ConfirmClose(() => throw new InvalidOperationException("Nothing to ask."));

        // Assert
        mayClose.ShouldBeTrue();
    }

    [Fact]
    public void EngineStatusText_FollowsStatusThroughLoadingToReadyOnCpu()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        RaiseEngineStatus(TranscriberStatus.Loading);
        var whileLoading = sut.EngineStatusText;
        A.CallTo(() => _transcriber.ActiveBackend).Returns("CPU");
        RaiseEngineStatus(TranscriberStatus.Ready);

        // Assert
        whileLoading.ShouldBe(ModelSectionViewModel.LoadingText);
        sut.EngineStatusText.ShouldBe("Ready on CPU");
    }

    [Fact]
    public void EngineStatusText_Failed_ShowsFailureMessage()
    {
        // Arrange
        var sut = CreateSut();
        A.CallTo(() => _transcriber.FailureMessage).Returns(TranscribeCppTranscriber.BackendFailedMessage);

        // Act
        RaiseEngineStatus(TranscriberStatus.Failed);

        // Assert
        sut.EngineStatusText.ShouldBe(TranscribeCppTranscriber.BackendFailedMessage);
    }

    [Fact]
    public void ModelInstalledElsewhere_ShowsInstalled()
    {
        // Arrange
        var sut = CreateSut();
        var item = Item(sut, SmallModelId);
        _installed.Add(SmallModelId);

        // Act
        _modelStore.ModelInstalled += Raise.With(_modelStore, item.Model);

        // Assert
        item.IsInstalled.ShouldBeTrue();
    }

    [Fact]
    public void IsSelected_NotInstalledModel_IsNotSelected()
    {
        // Arrange
        var sut = CreateSut();
        var item = Item(sut, SmallModelId);

        // Act
        item.IsSelected = true;

        // Assert
        item.IsSelected.ShouldBeFalse();
        sut.SelectedModelId.ShouldBe(DefaultModelId);
    }

    [Fact]
    public void Rebase_SavedModelChangedElsewhere_MovesActiveMarkerAndUneditedSelection()
    {
        // Arrange
        var sut = CreateSut();
        var previous = new AppSettings();

        // Act
        sut.Rebase(previous, previous with {Model = new ModelSettings(MediumModelId)});

        // Assert
        sut.SelectedModelId.ShouldBe(MediumModelId);
        Item(sut, MediumModelId).IsActive.ShouldBeTrue();
        Item(sut, DefaultModelId).IsActive.ShouldBeFalse();
        Item(sut, DefaultModelId).ShowsDelete.ShouldBeTrue();
    }

    private static ModelItemViewModel Item(ModelSectionViewModel sut, string modelId)
    {
        return sut.Items.Single(item => item.Model.Id == modelId);
    }

    private ModelSectionViewModel CreateSut()
    {
        return new ModelSectionViewModel(new AppSettings(), _modelStore, _transcriber, _applicationStopping.Token,
            model =>
            {
                _deleteConfirmations.Add(model);
                return _confirmDelete;
            }, action => action());
    }

    private void RaiseEngineStatus(TranscriberStatus status)
    {
        A.CallTo(() => _transcriber.Status).Returns(status);
        _transcriber.StatusChanged += Raise.With(_transcriber, status);
    }
}
