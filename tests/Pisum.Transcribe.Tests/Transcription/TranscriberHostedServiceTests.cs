using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Transcription;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.Transcription;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class TranscriberHostedServiceTests
{
    private static readonly SpeechModel SelectedModel = ModelCatalog.Resolve("canary-180m-flash-q8_0");
    private static readonly SpeechModel OtherModel = ModelCatalog.Resolve("canary-1b-v2-q4_k_m");

    private readonly ITranscriber _transcriber = A.Fake<ITranscriber>();
    private readonly IModelStore _modelStore = A.Fake<IModelStore>();
    private readonly ISettingsStore _settingsStore = A.Fake<ISettingsStore>();
    private readonly ITrayIconService _trayIcon = A.Fake<ITrayIconService>();
    private readonly TranscriberHostedService _sut;

    public TranscriberHostedServiceTests()
    {
        A.CallTo(() => _settingsStore.Current).Returns(new AppSettings
        {
            Model = new ModelSettings(SelectedModel.Id),
            Transcription = new TranscriptionSettings(BackendPreference.Cpu),
        });
        _sut = new TranscriberHostedService(_transcriber, _modelStore, _settingsStore, _trayIcon, action => action());
    }

    [Fact]
    public async Task StartAsync_SelectedModelInstalled_LoadsItWithBackendFromSettings()
    {
        // Arrange
        A.CallTo(() => _modelStore.IsInstalled(SelectedModel)).Returns(true);

        // Act
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        A.CallTo(() => _transcriber.LoadAsync(SelectedModel, BackendPreference.Cpu, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task StartAsync_SelectedModelNotInstalled_DoesNotLoad()
    {
        // Arrange
        A.CallTo(() => _modelStore.IsInstalled(A<SpeechModel>._)).Returns(false);

        // Act
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        A.CallTo(() => _transcriber.LoadAsync(A<SpeechModel>._, A<BackendPreference>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task ModelInstalled_SelectedModel_LoadsIt()
    {
        // Arrange
        A.CallTo(() => _modelStore.IsInstalled(A<SpeechModel>._)).Returns(false);
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        _modelStore.ModelInstalled += Raise.With(_modelStore, SelectedModel);

        // Assert
        A.CallTo(() => _transcriber.LoadAsync(SelectedModel, BackendPreference.Cpu, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ModelInstalled_OtherModel_DoesNotLoad()
    {
        // Arrange
        A.CallTo(() => _modelStore.IsInstalled(A<SpeechModel>._)).Returns(false);
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        _modelStore.ModelInstalled += Raise.With(_modelStore, OtherModel);

        // Assert
        A.CallTo(() => _transcriber.LoadAsync(A<SpeechModel>._, A<BackendPreference>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task ModelInstalled_AfterStop_DoesNotLoad()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        await _sut.StopAsync(TestContext.Current.CancellationToken);

        // Act
        _modelStore.ModelInstalled += Raise.With(_modelStore, SelectedModel);

        // Assert
        A.CallTo(() => _transcriber.LoadAsync(A<SpeechModel>._, A<BackendPreference>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Theory]
    [InlineData(nameof(TranscriberStatus.Loading))]
    [InlineData(nameof(TranscriberStatus.Ready))]
    public async Task StatusChanged_LoadingOrReady_LeavesTrayToDictationFeedback(string status)
    {
        // Arrange
        A.CallTo(() => _transcriber.ActiveBackend).Returns("CPU");
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        _transcriber.StatusChanged += Raise.With(_transcriber, Enum.Parse<TranscriberStatus>(status));

        // Assert
        A.CallTo(_trayIcon).MustNotHaveHappened();
    }

    [Fact]
    public async Task StatusChanged_Failed_NotifiesWithFailureMessageWithoutChangingToolTip()
    {
        // Arrange
        A.CallTo(() => _transcriber.FailureMessage).Returns(TranscribeCppTranscriber.DamagedModelMessage);
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        _transcriber.StatusChanged += Raise.With(_transcriber, TranscriberStatus.Failed);

        // Assert
        A.CallTo(() => _trayIcon.ShowNotification("Model failed to load", TranscribeCppTranscriber.DamagedModelMessage))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _trayIcon.SetStatus(A<System.Drawing.Icon>._, A<string>._)).MustNotHaveHappened();
    }
}
