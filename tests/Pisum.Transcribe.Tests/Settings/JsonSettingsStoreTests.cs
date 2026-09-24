using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.TextInsertion;
using Pisum.Transcribe.Transcription;

namespace Pisum.Transcribe.Tests.Settings;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly ILogger<JsonSettingsStore> _logger = A.Fake<ILogger<JsonSettingsStore>>();
    private readonly string _settingsFile;
    private readonly JsonSettingsStore _sut;

    public JsonSettingsStoreTests()
    {
        var paths = new AppPaths(_root.Path);
        paths.EnsureRootExists();
        _settingsFile = Path.Combine(_root.Path, "settings.json");
        _sut = new JsonSettingsStore(paths, _logger);
    }

    public void Dispose()
    {
        _root.Dispose();
    }

    [Fact]
    public void Load_FileMissing_UsesDefaults()
    {
        // Act
        _sut.Load();

        // Assert
        _sut.Current.ShouldBe(new AppSettings());
        _sut.Current.SchemaVersion.ShouldBe(2);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    public void Load_EmptyObjectOrNull_UsesDefaults(string json)
    {
        // Arrange
        File.WriteAllText(_settingsFile, json);

        // Act
        _sut.Load();

        // Assert
        _sut.Current.ShouldBe(new AppSettings());
    }

    [Fact]
    public void Load_PartialFile_KeepsValuesFromFile()
    {
        // Arrange
        File.WriteAllText(_settingsFile, """{ "schemaVersion": 7 }""");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.SchemaVersion.ShouldBe(7);
    }

    [Fact]
    public void Load_ModelSectionMissing_UsesDefaultModel()
    {
        // Arrange
        File.WriteAllText(_settingsFile, """{ "schemaVersion": 1 }""");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.Model.SelectedModelId.ShouldBe("canary-1b-v2-q8_0");
    }

    [Fact]
    public void Load_ModelSectionNull_UsesDefaultModel()
    {
        // Arrange
        File.WriteAllText(_settingsFile, """{ "schemaVersion": 1, "model": null }""");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.Model.ShouldNotBeNull();
        _sut.Current.Model.SelectedModelId.ShouldBe("canary-1b-v2-q8_0");
    }

    [Fact]
    public void Load_TranscriptionSectionMissing_UsesTranscriptionDefaults()
    {
        // Arrange
        File.WriteAllText(_settingsFile, """{ "schemaVersion": 1 }""");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.Transcription.Backend.ShouldBe(BackendPreference.Auto);
        _sut.Current.Transcription.Task.ShouldBe(TranscriptionTask.Translate);
        _sut.Current.Transcription.SourceLanguage.ShouldBe("de");
        _sut.Current.Transcription.TargetLanguage.ShouldBe("en");
    }

    [Fact]
    public void Load_TranscriptionSectionNull_UsesTranscriptionDefaults()
    {
        // Arrange
        File.WriteAllText(_settingsFile, """{ "schemaVersion": 1, "transcription": null }""");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.Transcription.ShouldBe(new TranscriptionSettings());
    }

    [Fact]
    public void Load_TranscriptionSectionPartial_KeepsValuesFromFileAndDefaultsTheRest()
    {
        // Arrange
        File.WriteAllText(_settingsFile, """{ "transcription": { "backend": "cpu", "task": "transcribe" } }""");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.Transcription.ShouldBe(new TranscriptionSettings(BackendPreference.Cpu,
            TranscriptionTask.Transcribe));
    }

    [Fact]
    public void Load_RecordingSectionMissing_UsesDefaultHotkey()
    {
        // Arrange
        File.WriteAllText(_settingsFile, """{ "schemaVersion": 1 }""");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.Recording.Hotkey.ShouldBe([HotkeyParser.DefaultKeyName]);
    }

    [Fact]
    public void Load_RecordingSectionNull_UsesDefaultHotkey()
    {
        // Arrange
        File.WriteAllText(_settingsFile, """{ "schemaVersion": 1, "recording": null }""");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.Recording.ShouldBe(new RecordingSettings());
        _sut.Current.Recording.Hotkey.ShouldBe([HotkeyParser.DefaultKeyName]);
    }

    [Fact]
    public void Load_TextInsertionSectionMissing_UsesTextInsertionDefaults()
    {
        // Arrange
        File.WriteAllText(_settingsFile, """{ "schemaVersion": 1 }""");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.TextInsertion.Method.ShouldBe(InsertionMethod.ClipboardPaste);
        _sut.Current.TextInsertion.RestoreClipboard.ShouldBeTrue();
    }

    [Fact]
    public void Load_TextInsertionSectionNull_UsesTextInsertionDefaults()
    {
        // Arrange
        File.WriteAllText(_settingsFile, """{ "schemaVersion": 1, "textInsertion": null }""");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.TextInsertion.ShouldBe(new TextInsertionSettings());
    }

    [Fact]
    public void Load_VoiceActivitySectionMissing_EnablesVoiceActivity()
    {
        // Arrange
        File.WriteAllText(_settingsFile, """{ "schemaVersion": 1 }""");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.VoiceActivity.Enabled.ShouldBeTrue();
    }

    [Fact]
    public void Load_VoiceActivitySectionNull_EnablesVoiceActivity()
    {
        // Arrange
        File.WriteAllText(_settingsFile, """{ "schemaVersion": 1, "voiceActivity": null }""");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.VoiceActivity.ShouldBe(new VoiceActivitySettings());
        _sut.Current.VoiceActivity.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task SaveAsync_VoiceActivityDisabled_RoundTripsAsVoiceActivitySection()
    {
        // Arrange
        var settings = new AppSettings {VoiceActivity = new VoiceActivitySettings(false)};
        await _sut.SaveAsync(settings, TestContext.Current.CancellationToken);
        var otherStore = new JsonSettingsStore(new AppPaths(_root.Path), _logger);

        // Act
        otherStore.Load();

        // Assert
        otherStore.Current.VoiceActivity.Enabled.ShouldBeFalse();
        var json = await File.ReadAllTextAsync(_settingsFile, TestContext.Current.CancellationToken);
        json.ShouldContain("\"voiceActivity\": {");
        json.ShouldContain("\"enabled\": false");
    }

    [Fact]
    public void Load_UpdatesSectionMissing_ChecksAutomatically()
    {
        // Arrange
        File.WriteAllText(_settingsFile, """{ "schemaVersion": 1 }""");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.Updates.CheckAutomatically.ShouldBeTrue();
    }

    [Fact]
    public void Load_UpdatesSectionNull_ChecksAutomatically()
    {
        // Arrange
        File.WriteAllText(_settingsFile, """{ "schemaVersion": 1, "updates": null }""");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.Updates.ShouldBe(new UpdateSettings());
        _sut.Current.Updates.CheckAutomatically.ShouldBeTrue();
    }

    [Fact]
    public async Task SaveAsync_CheckAutomaticallyOff_RoundTripsAsUpdatesSection()
    {
        // Arrange
        var settings = new AppSettings {Updates = new UpdateSettings(false)};
        await _sut.SaveAsync(settings, TestContext.Current.CancellationToken);
        var otherStore = new JsonSettingsStore(new AppPaths(_root.Path), _logger);

        // Act
        otherStore.Load();

        // Assert
        otherStore.Current.Updates.CheckAutomatically.ShouldBeFalse();
        var json = await File.ReadAllTextAsync(_settingsFile, TestContext.Current.CancellationToken);
        json.ShouldContain("\"updates\": {");
        json.ShouldContain("\"checkAutomatically\": false");
    }

    [Fact]
    public async Task SaveAsync_DefaultTextInsertionSettings_RoundTripWithCamelCaseEnumValues()
    {
        // Arrange
        await _sut.SaveAsync(new AppSettings(), TestContext.Current.CancellationToken);
        var otherStore = new JsonSettingsStore(new AppPaths(_root.Path), _logger);

        // Act
        otherStore.Load();

        // Assert
        otherStore.Current.TextInsertion.ShouldBe(new TextInsertionSettings(InsertionMethod.ClipboardPaste, true));
        var json = await File.ReadAllTextAsync(_settingsFile, TestContext.Current.CancellationToken);
        json.ShouldContain("\"textInsertion\": {");
        json.ShouldContain("\"method\": \"clipboardPaste\"");
        json.ShouldContain("\"restoreClipboard\": true");
    }

    [Fact]
    public async Task SaveAsync_ChangedTextInsertionSettings_RoundTripWithCamelCaseEnumValues()
    {
        // Arrange
        var settings = new AppSettings {TextInsertion = new TextInsertionSettings(InsertionMethod.TypeText, false)};
        await _sut.SaveAsync(settings, TestContext.Current.CancellationToken);
        var otherStore = new JsonSettingsStore(new AppPaths(_root.Path), _logger);

        // Act
        otherStore.Load();

        // Assert
        otherStore.Current.TextInsertion.ShouldBe(settings.TextInsertion);
        var json = await File.ReadAllTextAsync(_settingsFile, TestContext.Current.CancellationToken);
        json.ShouldContain("\"method\": \"typeText\"");
        json.ShouldContain("\"restoreClipboard\": false");
    }

    [Fact]
    public async Task SaveAsync_Hotkey_RoundTripsAsRecordingSection()
    {
        // Arrange
        var settings = new AppSettings {Recording = new RecordingSettings {Hotkey = ["VcLeftControl", "VcLeftMeta"]}};
        await _sut.SaveAsync(settings, TestContext.Current.CancellationToken);
        var otherStore = new JsonSettingsStore(new AppPaths(_root.Path), _logger);

        // Act
        otherStore.Load();

        // Assert
        otherStore.Current.Recording.ShouldBe(settings.Recording);
        (await File.ReadAllTextAsync(_settingsFile, TestContext.Current.CancellationToken)).ShouldContain(
            "\"hotkey\": [");
    }

    [Fact]
    public async Task SaveAsync_DefaultTranscriptionSettings_RoundTripWithLowercaseEnumValues()
    {
        // Arrange
        await _sut.SaveAsync(new AppSettings(), TestContext.Current.CancellationToken);
        var otherStore = new JsonSettingsStore(new AppPaths(_root.Path), _logger);

        // Act
        otherStore.Load();

        // Assert
        otherStore.Current.Transcription.ShouldBe(new TranscriptionSettings());
        var json = await File.ReadAllTextAsync(_settingsFile, TestContext.Current.CancellationToken);
        json.ShouldContain("\"backend\": \"auto\"");
        json.ShouldContain("\"task\": \"translate\"");
        json.ShouldContain("\"sourceLanguage\": \"de\"");
        json.ShouldContain("\"targetLanguage\": \"en\"");
    }

    [Fact]
    public async Task SaveAsync_ChangedTranscriptionSettings_RoundTripWithLowercaseEnumValues()
    {
        // Arrange
        var settings = new AppSettings
        {
            Transcription = new TranscriptionSettings(BackendPreference.Gpu, TranscriptionTask.Transcribe, "en",
                "de"),
        };
        await _sut.SaveAsync(settings, TestContext.Current.CancellationToken);
        var otherStore = new JsonSettingsStore(new AppPaths(_root.Path), _logger);

        // Act
        otherStore.Load();

        // Assert
        otherStore.Current.Transcription.ShouldBe(settings.Transcription);
        var json = await File.ReadAllTextAsync(_settingsFile, TestContext.Current.CancellationToken);
        json.ShouldContain("\"backend\": \"gpu\"");
        json.ShouldContain("\"task\": \"transcribe\"");
    }

    [Fact]
    public void Load_UnknownProperties_AreIgnored()
    {
        // Arrange
        File.WriteAllText(_settingsFile,
            """{ "schemaVersion": 7, "unknownSection": { "value": 1 }, "unknownValue": "x" }""");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.SchemaVersion.ShouldBe(7);
    }

    [Fact]
    public void Load_InvalidJson_RenamesFileLogsWarningAndUsesDefaults()
    {
        // Arrange
        const string invalidJson = """{ "schemaVersion": """;
        File.WriteAllText(_settingsFile, invalidJson);
        File.WriteAllText(_settingsFile + ".corrupt", "older corrupt file");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.ShouldBe(new AppSettings());
        File.Exists(_settingsFile).ShouldBeFalse();
        File.ReadAllText(_settingsFile + ".corrupt").ShouldBe(invalidJson);
        A.CallTo(_logger)
            .Where(call => call.Method.Name == nameof(ILogger.Log) && call.GetArgument<LogLevel>(0) == LogLevel.Warning)
            .MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData("\"schemaVersion\": 1, ")]
    [InlineData("")]
    public void Load_OlderFormatWithVulkanBackend_ReadsGpuAndKeepsOtherValues(string version)
    {
        // Arrange
        File.WriteAllText(_settingsFile, $$"""
            { {{version}}"model": { "selectedModelId": "canary-180m-flash-q8_0" },
              "transcription": { "task": "transcribe", "sourceLanguage": "en", "targetLanguage": "de",
                                 "backend": "vulkan" },
              "recording": { "hotkey": ["VcLeftControl", "VcLeftMeta"] } }
            """);

        // Act
        _sut.Load();

        // Assert
        _sut.Current.SchemaVersion.ShouldBe(2);
        _sut.Current.Model.SelectedModelId.ShouldBe("canary-180m-flash-q8_0");
        _sut.Current.Transcription.ShouldBe(new TranscriptionSettings(BackendPreference.Gpu,
            TranscriptionTask.Transcribe, "en", "de"));
        _sut.Current.Recording.Hotkey.ShouldBe(["VcLeftControl", "VcLeftMeta"]);
        File.Exists(_settingsFile + ".corrupt").ShouldBeFalse();
    }

    [Fact]
    public void Load_OlderFormat_LeavesFileUnchanged()
    {
        // Arrange
        const string json = """{ "schemaVersion": 1, "transcription": { "backend": "vulkan" } }""";
        File.WriteAllText(_settingsFile, json);

        // Act
        _sut.Load();

        // Assert
        File.ReadAllText(_settingsFile).ShouldBe(json);
    }

    [Fact]
    public async Task SaveAsync_AfterLoadingOlderFormat_WritesCurrentFormat()
    {
        // Arrange
        File.WriteAllText(_settingsFile, """{ "schemaVersion": 1, "transcription": { "backend": "vulkan" } }""");
        _sut.Load();

        // Act
        await _sut.SaveAsync(_sut.Current, TestContext.Current.CancellationToken);

        // Assert
        var json = await File.ReadAllTextAsync(_settingsFile, TestContext.Current.CancellationToken);
        json.ShouldContain("\"schemaVersion\": 2");
        json.ShouldContain("\"backend\": \"gpu\"");
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1 }""")]
    [InlineData("""{ "schemaVersion": 1, "transcription": { "task": "transcribe" } }""")]
    public void Load_OlderFormatWithoutBackend_UsesDefaultBackend(string json)
    {
        // Arrange
        File.WriteAllText(_settingsFile, json);

        // Act
        _sut.Load();

        // Assert
        _sut.Current.SchemaVersion.ShouldBe(2);
        _sut.Current.Transcription.Backend.ShouldBe(BackendPreference.Auto);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1, "transcription": { "backend": "metal" } }""")]
    [InlineData("""{ "schemaVersion": 3, "transcription": { "backend": "vulkan" } }""")]
    [InlineData("""{ "schemaVersion": "1", "transcription": { "backend": "vulkan" } }""")]
    public void Load_UnknownBackendOrNotMigrated_RenamesFileAndUsesDefaults(string json)
    {
        // Arrange
        File.WriteAllText(_settingsFile, json);

        // Act
        _sut.Load();

        // Assert
        _sut.Current.ShouldBe(new AppSettings());
        File.Exists(_settingsFile).ShouldBeFalse();
        File.ReadAllText(_settingsFile + ".corrupt").ShouldBe(json);
    }

    [Fact]
    public void Load_NewerFormat_IsReadWithoutMigration()
    {
        // Arrange
        File.WriteAllText(_settingsFile, """{ "schemaVersion": 3, "transcription": { "backend": "gpu" } }""");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.SchemaVersion.ShouldBe(3);
        _sut.Current.Transcription.Backend.ShouldBe(BackendPreference.Gpu);
    }

    [Fact]
    public void Load_StaleTempFile_IsNotUsed()
    {
        // Arrange
        File.WriteAllText(_settingsFile, """{ "schemaVersion": 7 }""");
        File.WriteAllText(_settingsFile + ".tmp", """{ "schemaVersion": 99""");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.SchemaVersion.ShouldBe(7);
    }

    [Fact]
    public async Task SaveAsync_Settings_WritesCamelCaseJsonAndLeavesNoTempFile()
    {
        // Arrange
        var settings = new AppSettings {SchemaVersion = 7};

        // Act
        await _sut.SaveAsync(settings, TestContext.Current.CancellationToken);

        // Assert
        _sut.Current.ShouldBe(settings);
        File.Exists(_settingsFile + ".tmp").ShouldBeFalse();
        (await File.ReadAllTextAsync(_settingsFile, TestContext.Current.CancellationToken)).ShouldContain(
            "\"schemaVersion\": 7");
    }

    [Fact]
    public async Task SaveAsync_ThenLoad_RoundTrips()
    {
        // Arrange
        var settings = new AppSettings {SchemaVersion = 7};
        await _sut.SaveAsync(settings, TestContext.Current.CancellationToken);
        var otherStore = new JsonSettingsStore(new AppPaths(_root.Path), _logger);

        // Act
        otherStore.Load();

        // Assert
        otherStore.Current.ShouldBe(settings);
    }

    [Fact]
    public async Task SaveAsync_SelectedModel_RoundTripsAsModelSection()
    {
        // Arrange
        var settings = new AppSettings {Model = new ModelSettings("canary-180m-flash-q8_0")};
        await _sut.SaveAsync(settings, TestContext.Current.CancellationToken);
        var otherStore = new JsonSettingsStore(new AppPaths(_root.Path), _logger);

        // Act
        otherStore.Load();

        // Assert
        otherStore.Current.Model.SelectedModelId.ShouldBe("canary-180m-flash-q8_0");
        (await File.ReadAllTextAsync(_settingsFile, TestContext.Current.CancellationToken)).ShouldContain(
            "\"selectedModelId\": \"canary-180m-flash-q8_0\"");
    }

    [Fact]
    public async Task SaveAsync_Succeeds_RaisesChangedOnceWithPreviousAndCurrent()
    {
        // Arrange
        _sut.Load();
        var previous = _sut.Current;
        var settings = new AppSettings {Model = new ModelSettings("canary-180m-flash-q8_0")};
        var raised = new List<SettingsChangedEventArgs>();
        _sut.Changed += (_, e) => raised.Add(e);

        // Act
        await _sut.SaveAsync(settings, TestContext.Current.CancellationToken);

        // Assert
        var change = raised.ShouldHaveSingleItem();
        change.Previous.ShouldBeSameAs(previous);
        change.Current.ShouldBeSameAs(settings);
    }

    [Fact]
    public async Task SaveAsync_Fails_DoesNotRaiseChangedAndKeepsCurrent()
    {
        // Arrange
        _sut.Load();
        var previous = _sut.Current;
        var raised = 0;
        _sut.Changed += (_, _) => raised++;

        // A folder where the file belongs makes the final replace fail.
        Directory.CreateDirectory(_settingsFile);

        // Act
        var exception = await Record.ExceptionAsync(() =>
            _sut.SaveAsync(new AppSettings {SchemaVersion = 7}, TestContext.Current.CancellationToken));

        // Assert: Windows reports the folder as access denied, and macOS as an I/O error.
        (exception is UnauthorizedAccessException or IOException).ShouldBeTrue(exception?.ToString());
        raised.ShouldBe(0);
        _sut.Current.ShouldBeSameAs(previous);
    }
}
