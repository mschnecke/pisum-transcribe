using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SettingsWindow;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Transcription;
using SharpHook.Data;

namespace Pisum.Transcribe.Tests.SettingsWindow;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class SettingsApplierTests
{
    private static readonly SpeechModel DefaultModel = ModelCatalog.Resolve(null);
    private static readonly SpeechModel SmallModel = ModelCatalog.Resolve("canary-180m-flash-q8_0");

    private readonly ISettingsStore _settingsStore = A.Fake<ISettingsStore>();
    private readonly IPushToTalkHotkey _hotkey = A.Fake<IPushToTalkHotkey>();
    private readonly ITranscriber _transcriber = A.Fake<ITranscriber>();
    private readonly IModelStore _modelStore = A.Fake<IModelStore>();
    private readonly SettingsApplier _sut;

    public SettingsApplierTests()
    {
        A.CallTo(() => _modelStore.IsInstalled(A<SpeechModel>._)).Returns(true);
        _sut = new SettingsApplier(_settingsStore, _hotkey, _transcriber, _modelStore,
            NullLogger<SettingsApplier>.Instance);
    }

    [Fact]
    public async Task Changed_Hotkey_SetsHotkeyOnly()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        var previous = new AppSettings();
        var current = previous with
        {
            Recording = new RecordingSettings {Hotkey = ["VcLeftControl", "VcLeftMeta"]},
        };

        // Act
        RaiseChanged(previous, current);

        // Assert
        A.CallTo(() => _hotkey.SetHotkey(A<IReadOnlySet<KeyCode>>.That.Matches(keys =>
                keys.SetEquals(new[] {KeyCode.VcLeftControl, KeyCode.VcLeftMeta}))))
            .MustHaveHappenedOnceExactly();
        A.CallTo(_transcriber).MustNotHaveHappened();
    }

    [Fact]
    public async Task Changed_InstalledModel_LoadsNewModelWithSavedBackend()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        var previous = new AppSettings {Transcription = new TranscriptionSettings(BackendPreference.Cpu)};
        var current = previous with {Model = new ModelSettings(SmallModel.Id)};

        // Act
        RaiseChanged(previous, current);

        // Assert
        A.CallTo(() => _transcriber.LoadAsync(SmallModel, BackendPreference.Cpu, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(_hotkey).MustNotHaveHappened();
    }

    [Fact]
    public async Task Changed_ModelNotInstalled_LoadsNothing()
    {
        // Arrange
        A.CallTo(() => _modelStore.IsInstalled(SmallModel)).Returns(false);
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        var previous = new AppSettings();
        var current = previous with {Model = new ModelSettings(SmallModel.Id)};

        // Act
        RaiseChanged(previous, current);

        // Assert
        A.CallTo(_transcriber).MustNotHaveHappened();
        A.CallTo(_hotkey).MustNotHaveHappened();
    }

    [Fact]
    public async Task Changed_Backend_ReloadsSameModelWithNewBackend()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        var previous = new AppSettings();
        var current = previous with {Transcription = previous.Transcription with {Backend = BackendPreference.Cpu}};

        // Act
        RaiseChanged(previous, current);

        // Assert
        A.CallTo(() => _transcriber.LoadAsync(DefaultModel, BackendPreference.Cpu, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Changed_BackendWhileSelectedModelNotInstalled_LoadsNothing()
    {
        // Arrange
        A.CallTo(() => _modelStore.IsInstalled(DefaultModel)).Returns(false);
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        var previous = new AppSettings();
        var current = previous with {Transcription = previous.Transcription with {Backend = BackendPreference.Cpu}};

        // Act
        RaiseChanged(previous, current);

        // Assert
        A.CallTo(_transcriber).MustNotHaveHappened();
    }

    [Fact]
    public async Task Changed_LanguagesAndTextInsertionOnly_CallsNoService()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        var previous = new AppSettings();
        var current = previous with
        {
            Transcription = previous.Transcription with {Task = TranscriptionTask.Transcribe, SourceLanguage = "fr"},
            TextInsertion = previous.TextInsertion with {RestoreClipboard = false},
        };

        // Act
        RaiseChanged(previous, current);

        // Assert
        A.CallTo(_transcriber).MustNotHaveHappened();
        A.CallTo(_hotkey).MustNotHaveHappened();
    }

    [Fact]
    public async Task Changed_AfterStop_CallsNoService()
    {
        // Arrange
        await _sut.StartAsync(TestContext.Current.CancellationToken);
        await _sut.StopAsync(TestContext.Current.CancellationToken);
        var previous = new AppSettings();
        var current = previous with {Model = new ModelSettings(SmallModel.Id)};

        // Act
        RaiseChanged(previous, current);

        // Assert
        A.CallTo(_transcriber).MustNotHaveHappened();
    }

    private void RaiseChanged(AppSettings previous, AppSettings current)
    {
        _settingsStore.Changed += Raise.With(new SettingsChangedEventArgs(previous, current));
    }
}
