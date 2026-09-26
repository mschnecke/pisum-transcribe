using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SettingsWindow;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.TextInsertion;
using Pisum.Transcribe.Transcription;
using SharpHook.Data;

namespace Pisum.Transcribe.Tests.SettingsWindow;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class SettingsViewModelTests
{
    private const string SmallModelId = "canary-180m-flash-q8_0";

    private readonly FakeSettingsStore _settingsStore = new(new AppSettings());
    private readonly IStartupRegistration _startupRegistration = A.Fake<IStartupRegistration>();
    private readonly IModelStore _modelStore = A.Fake<IModelStore>();
    private readonly ITranscriber _transcriber = A.Fake<ITranscriber>();
    private readonly IPushToTalkHotkey _hotkey = A.Fake<IPushToTalkHotkey>();
    private readonly IHostApplicationLifetime _lifetime = A.Fake<IHostApplicationLifetime>();

    public SettingsViewModelTests()
    {
        A.CallTo(() => _modelStore.IsInstalled(A<SpeechModel>._)).Returns(true);
        A.CallTo(() => _transcriber.Status).Returns(TranscriberStatus.Ready);
        A.CallTo(() => _transcriber.ActiveBackend).Returns("Vulkan");
    }

    [Fact]
    public void Constructor_SavedSettings_ShowsThemWithSaveDisabled()
    {
        // Act
        var sut = CreateSut();

        // Assert
        sut.Dictation.Task.ShouldBe(TranscriptionTask.Translate);
        sut.Dictation.SourceLanguage.ShouldBe("de");
        sut.Dictation.TargetLanguage.ShouldBe("en");
        sut.Dictation.HotkeyName.ShouldBe(HotkeyText.Format(HotkeyParser.DefaultHotkey));
        sut.Model.SelectedModelId.ShouldBe(ModelCatalog.DefaultModelId);
        sut.TextInsertion.Method.ShouldBe(InsertionMethod.ClipboardPaste);
        sut.General.StartAtSignIn.ShouldBeFalse();
        sut.HasChanges.ShouldBeFalse();
        sut.SaveCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void Close_AfterEdits_LeavesStoreUnchanged()
    {
        // Arrange
        var sut = CreateSut();
        sut.Dictation.Task = TranscriptionTask.Transcribe;

        // Act
        var mayClose = sut.ConfirmClose(() => throw new InvalidOperationException("Nothing to ask."));
        sut.OnClosed();

        // Assert
        mayClose.ShouldBeTrue();
        _settingsStore.Saves.ShouldBeEmpty();
        _settingsStore.Current.Transcription.Task.ShouldBe(TranscriptionTask.Translate);
    }

    [Fact]
    public void SelectModel_SmallModelWithPolishSource_ShowsErrorAndDisablesSave()
    {
        // Arrange
        var sut = CreateSut();
        sut.Dictation.SourceLanguage = "pl";

        // Act
        sut.Model.SelectModel(SmallModelId);

        // Assert
        sut.Dictation.SourceLanguageError.ShouldNotBeNull();
        sut.Dictation.HasErrors.ShouldBeTrue();
        sut.HasChanges.ShouldBeTrue();
        sut.SaveCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void SourceLanguage_SupportedAgain_ClearsErrorAndEnablesSave()
    {
        // Arrange
        var sut = CreateSut();
        sut.Dictation.SourceLanguage = "pl";
        sut.Model.SelectModel(SmallModelId);

        // Act
        sut.Dictation.SourceLanguage = "fr";

        // Assert
        sut.Dictation.HasErrors.ShouldBeFalse();
        sut.Dictation.TargetLanguage.ShouldBe("en");
        sut.SaveCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task SaveCommand_Edits_SavesChangedFieldsKeepsWindowOpenAndDisablesSave()
    {
        // Arrange
        var sut = CreateSut();
        sut.Dictation.Task = TranscriptionTask.Transcribe;
        sut.Model.Backend = BackendPreference.Cpu;
        sut.TextInsertion.Method = InsertionMethod.TypeText;

        // Act
        await sut.SaveCommand.ExecuteAsync(null);

        // Assert
        var saved = _settingsStore.Saves.ShouldHaveSingleItem();
        saved.ShouldBe(new AppSettings
        {
            Transcription = new TranscriptionSettings(BackendPreference.Cpu, TranscriptionTask.Transcribe),
            TextInsertion = new TextInsertionSettings(InsertionMethod.TypeText),
        });
        sut.SaveError.ShouldBeNull();
        sut.HasChanges.ShouldBeFalse();
        sut.SaveCommand.CanExecute(null).ShouldBeFalse();
        A.CallTo(() => _startupRegistration.SetEnabled(A<bool>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task SaveCommand_ModelSavedElsewhereWhileOnlyHotkeyEdited_KeepsExternalModel()
    {
        // Arrange
        var sut = CreateSut();
        RecordHotkey(sut, KeyCode.VcLeftControl, KeyCode.VcLeftMeta);
        var current = _settingsStore.Current;
        await _settingsStore.SaveAsync(current with {Model = new ModelSettings(SmallModelId)},
            TestContext.Current.CancellationToken);

        // Act
        await sut.SaveCommand.ExecuteAsync(null);

        // Assert
        sut.Model.SelectedModelId.ShouldBe(SmallModelId);
        _settingsStore.Current.Model.SelectedModelId.ShouldBe(SmallModelId);
        _settingsStore.Current.Recording.Hotkey.ShouldBe(["VcLeftControl", "VcLeftMeta"]);
    }

    [Fact]
    public async Task SettingsSavedElsewhere_FieldEditedInWindow_KeepsTheEdit()
    {
        // Arrange
        var sut = CreateSut();
        sut.Dictation.Task = TranscriptionTask.Transcribe;
        var current = _settingsStore.Current;

        // Act
        await _settingsStore.SaveAsync(current with
        {
            Transcription = current.Transcription with {Task = TranscriptionTask.Translate, SourceLanguage = "fr"},
        }, TestContext.Current.CancellationToken);

        // Assert
        sut.Dictation.Task.ShouldBe(TranscriptionTask.Transcribe);
        sut.Dictation.SourceLanguage.ShouldBe("fr");
    }

    [Fact]
    public void Constructor_VoiceActivitySavedAsDisabled_ShowsTrimSilenceCleared()
    {
        // Arrange
        var settingsStore = new FakeSettingsStore(new AppSettings {VoiceActivity = new VoiceActivitySettings(false)});

        // Act
        var sut = CreateSut(settingsStore);

        // Assert
        sut.Dictation.TrimSilence.ShouldBeFalse();
        sut.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task SaveCommand_TrimSilenceCleared_EnablesSaveAndSavesVoiceActivityDisabled()
    {
        // Arrange
        var sut = CreateSut();
        var trimSilenceBefore = sut.Dictation.TrimSilence;
        sut.Dictation.TrimSilence = false;
        var hasChanges = sut.HasChanges;
        var canSave = sut.SaveCommand.CanExecute(null);

        // Act
        await sut.SaveCommand.ExecuteAsync(null);

        // Assert
        trimSilenceBefore.ShouldBeTrue();
        hasChanges.ShouldBeTrue();
        canSave.ShouldBeTrue();
        var saved = _settingsStore.Saves.ShouldHaveSingleItem();
        saved.ShouldBe(new AppSettings {VoiceActivity = new VoiceActivitySettings(false)});
        sut.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task SettingsSavedElsewhere_TrimSilenceNotEdited_ShowsSavedValue()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await _settingsStore.SaveAsync(_settingsStore.Current with {VoiceActivity = new VoiceActivitySettings(false)},
            TestContext.Current.CancellationToken);

        // Assert
        sut.Dictation.TrimSilence.ShouldBeFalse();
        sut.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task SettingsSavedElsewhere_TrimSilenceEdited_KeepsTheEdit()
    {
        // Arrange
        var sut = CreateSut();
        sut.Dictation.TrimSilence = false;

        // Act
        await _settingsStore.SaveAsync(_settingsStore.Current with {Model = new ModelSettings(SmallModelId)},
            TestContext.Current.CancellationToken);

        // Assert
        sut.Dictation.TrimSilence.ShouldBeFalse();
        sut.Model.SelectedModelId.ShouldBe(SmallModelId);
        sut.HasChanges.ShouldBeTrue();
    }

    [Fact]
    public void Constructor_DefaultSettings_ChecksForUpdates()
    {
        // Act
        var sut = CreateSut();

        // Assert
        sut.General.CheckForUpdates.ShouldBeTrue();
        sut.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task Save_CheckForUpdatesTurnedOff_SavesUpdatesSectionOff()
    {
        // Arrange
        var sut = CreateSut();
        sut.General.CheckForUpdates = false;
        var canSave = sut.SaveCommand.CanExecute(null);

        // Act
        await sut.SaveCommand.ExecuteAsync(null);

        // Assert
        canSave.ShouldBeTrue();
        var saved = _settingsStore.Saves.ShouldHaveSingleItem();
        saved.ShouldBe(new AppSettings {Updates = new UpdateSettings(false)});
        sut.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task SettingsChangedElsewhere_CheckForUpdatesNotEdited_ShowsNewValue()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await _settingsStore.SaveAsync(_settingsStore.Current with {Updates = new UpdateSettings(false)},
            TestContext.Current.CancellationToken);

        // Assert
        sut.General.CheckForUpdates.ShouldBeFalse();
        sut.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public void TextInsertion_TypeText_DisablesRestoreClipboard()
    {
        // Arrange
        var sut = CreateSut();
        var canRestoreForPaste = sut.TextInsertion.CanRestoreClipboard;

        // Act
        sut.TextInsertion.IsTypeText = true;

        // Assert
        canRestoreForPaste.ShouldBeTrue();
        sut.TextInsertion.Method.ShouldBe(InsertionMethod.TypeText);
        sut.TextInsertion.CanRestoreClipboard.ShouldBeFalse();
    }

    [Fact]
    public async Task SaveCommand_StartAtSignInChanged_SetsItWithoutSavingSettings()
    {
        // Arrange
        A.CallTo(() => _startupRegistration.IsEnabled()).Returns(false).Once().Then.Returns(true);
        var sut = CreateSut();

        // Act
        sut.General.StartAtSignIn = true;
        await sut.SaveCommand.ExecuteAsync(null);

        // Assert
        A.CallTo(() => _startupRegistration.SetEnabled(true)).MustHaveHappenedOnceExactly();
        _settingsStore.Saves.ShouldBeEmpty();
        sut.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public async Task SaveCommand_StartAtSignInFails_ShowsErrorAndStillSavesSettings()
    {
        // Arrange
        A.CallTo(() => _startupRegistration.SetEnabled(A<bool>._)).Throws(new UnauthorizedAccessException());
        var sut = CreateSut();
        sut.Dictation.Task = TranscriptionTask.Transcribe;
        sut.General.StartAtSignIn = true;

        // Act
        await sut.SaveCommand.ExecuteAsync(null);

        // Assert
        sut.SaveError.ShouldBe(SettingsViewModel.StartupFailedMessage);
        _settingsStore.Current.Transcription.Task.ShouldBe(TranscriptionTask.Transcribe);
        sut.General.StartAtSignIn.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_StartsAtSignIn_ShowsTheOptionOn()
    {
        // Arrange
        A.CallTo(() => _startupRegistration.IsEnabled()).Returns(true);

        // Act
        var sut = CreateSut();

        // Assert
        sut.General.StartAtSignIn.ShouldBeTrue();
        sut.General.RequiresApproval.ShouldBeFalse();
        sut.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_RequiresApproval_ShowsTheOptionOffWithTheHint()
    {
        // Arrange
        A.CallTo(() => _startupRegistration.RequiresApproval()).Returns(true);

        // Act
        var sut = CreateSut();

        // Assert
        sut.General.StartAtSignIn.ShouldBeFalse();
        sut.General.RequiresApproval.ShouldBeTrue();
    }

    [Fact]
    public async Task SaveCommand_StartAtSignInChanged_ReadsTheApprovalAgain()
    {
        // Arrange
        A.CallTo(() => _startupRegistration.RequiresApproval()).Returns(true).Once().Then.Returns(false);
        A.CallTo(() => _startupRegistration.IsEnabled()).Returns(false).Once().Then.Returns(true);
        var sut = CreateSut();

        // Act
        sut.General.StartAtSignIn = true;
        await sut.SaveCommand.ExecuteAsync(null);

        // Assert
        sut.General.StartAtSignIn.ShouldBeTrue();
        sut.General.RequiresApproval.ShouldBeFalse();
    }

    [Fact]
    public void StartAtSignInLabel_Platform_NamesThePlatformsOption()
    {
        // Act
        var label = GeneralSectionViewModel.StartAtSignInLabel;

        // Assert
#if WINDOWS
        label.ShouldBe("Start with _Windows");
        SettingsViewModel.StartupFailedMessage.ShouldStartWith("Start with Windows");
#else
        label.ShouldBe("Open at _login");
        SettingsViewModel.StartupFailedMessage.ShouldStartWith("Open at login");
#endif
    }

    [Fact]
    public async Task SaveCommand_SettingsFileFails_ShowsErrorAndKeepsEdits()
    {
        // Arrange
        _settingsStore.SaveException = new IOException("Disk full.");
        var sut = CreateSut();
        sut.Dictation.Task = TranscriptionTask.Transcribe;
        sut.General.StartAtSignIn = true;

        // Act
        await sut.SaveCommand.ExecuteAsync(null);

        // Assert
        sut.SaveError.ShouldBe(SettingsViewModel.SaveFailedMessage);
        sut.HasChanges.ShouldBeTrue();
        sut.SaveCommand.CanExecute(null).ShouldBeTrue();
        A.CallTo(() => _startupRegistration.SetEnabled(A<bool>._)).MustNotHaveHappened();
    }

    [Fact]
    public void ChangeHotkey_KeysCaptured_ShowsNewHotkeyAndResumesPushToTalk()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        RecordHotkey(sut, KeyCode.VcLeftControl, KeyCode.VcLeftMeta);

        // Assert
        sut.Dictation.HotkeyName.ShouldBe(HotkeyText.Format([KeyCode.VcLeftControl, KeyCode.VcLeftMeta]));
        sut.Dictation.IsRecordingHotkey.ShouldBeFalse();
        sut.SaveCommand.CanExecute(null).ShouldBeTrue();
        A.CallTo(() => _hotkey.Suspend()).MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _hotkey.Resume()).MustHaveHappenedOnceExactly());
    }

    [Fact]
    public void ChangeHotkey_LetterAlone_ShowsErrorKeepsHotkeyAndResumes()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        RecordHotkey(sut, KeyCode.VcA);

        // Assert
        sut.Dictation.HotkeyError.ShouldBe(HotkeyKeyNames.Current.RejectedMessage);
        sut.Dictation.HotkeyName.ShouldBe(HotkeyText.Format(HotkeyParser.DefaultHotkey));
        sut.HasChanges.ShouldBeFalse();
        A.CallTo(() => _hotkey.Resume()).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void OnClosed_WhileRecordingHotkey_ResumesPushToTalkWithoutChange()
    {
        // Arrange
        var sut = CreateSut();
        sut.Dictation.ChangeHotkeyCommand.Execute(null);
        RaiseRawKey(KeyCode.VcLeftAlt, true);

        // Act
        sut.OnClosed();

        // Assert
        A.CallTo(() => _hotkey.Resume()).MustHaveHappenedOnceExactly();
        sut.Dictation.IsRecordingHotkey.ShouldBeFalse();
        sut.Dictation.HotkeyName.ShouldBe(HotkeyText.Format(HotkeyParser.DefaultHotkey));
    }

    [Fact]
    public void CancelHotkeyRecording_KeysArriveLater_IgnoresThem()
    {
        // Arrange
        var sut = CreateSut();
        sut.Dictation.ChangeHotkeyCommand.Execute(null);

        // Act
        sut.Dictation.CancelHotkeyRecording();
        RaiseRawKey(KeyCode.VcF13, true);
        RaiseRawKey(KeyCode.VcF13, false);

        // Assert
        sut.Dictation.HotkeyName.ShouldBe(HotkeyText.Format(HotkeyParser.DefaultHotkey));
        A.CallTo(() => _hotkey.Resume()).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task SettingsSavedElsewhere_AfterClose_AreIgnored()
    {
        // Arrange
        var sut = CreateSut();
        sut.OnClosed();

        // Act
        await _settingsStore.SaveAsync(_settingsStore.Current with {Model = new ModelSettings(SmallModelId)},
            TestContext.Current.CancellationToken);

        // Assert
        sut.Model.SelectedModelId.ShouldBe(ModelCatalog.DefaultModelId);
    }

    private SettingsViewModel CreateSut(FakeSettingsStore? settingsStore = null)
    {
        return new SettingsViewModel(settingsStore ?? _settingsStore, _startupRegistration, _modelStore, _transcriber,
            _hotkey, _lifetime, _ => true, NullLogger<SettingsViewModel>.Instance, new InlineUiDispatcher());
    }

    private void RecordHotkey(SettingsViewModel sut, params KeyCode[] keys)
    {
        sut.Dictation.ChangeHotkeyCommand.Execute(null);
        foreach (var key in keys)
        {
            RaiseRawKey(key, true);
        }

        foreach (var key in keys)
        {
            RaiseRawKey(key, false);
        }
    }

    private void RaiseRawKey(KeyCode key, bool isPressed)
    {
        _hotkey.RawKey += Raise.With(new RawKeyEventArgs(key, isPressed));
    }
}
