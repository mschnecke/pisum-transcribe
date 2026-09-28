using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Dictation;
using Pisum.Transcribe.Notifications;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.Tests.SettingsWindow;
using Pisum.Transcribe.Transcription;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.Dictation;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class TaskMenuTests
{
    private readonly ITrayIconService _trayIcon = A.Fake<ITrayIconService>();
    private readonly INotifier _notifier = A.Fake<INotifier>();
    private readonly IHostApplicationLifetime _lifetime = A.Fake<IHostApplicationLifetime>();
    private readonly CapturingLogger<TaskMenu> _logger = new();
    private readonly List<MenuItem> _items = [];

    public TaskMenuTests()
    {
        A.CallTo(() => _trayIcon.AddMenuItem(A<Func<string>>._, A<Action>._, A<Func<bool>?>._, A<Func<bool>?>._))
            .Invokes((Func<string> header, Action onClick, Func<bool>? _, Func<bool>? isChecked) =>
                _items.Add(new MenuItem(header, onClick, isChecked)));
    }

    [Fact]
    public async Task StartAsync_TranslateGermanToEnglish_AddsRadioItemsWithTranslateChecked()
    {
        // Arrange
        var sut = CreateSut(new FakeSettingsStore(new AppSettings()));

        // Act
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        _items.Count.ShouldBe(2);
        _items[0].Header().ShouldBe("Transcribe (German)");
        _items[0].IsChecked.ShouldNotBeNull()().ShouldBeFalse();
        _items[1].Header().ShouldBe("Translate (German → English)");
        _items[1].IsChecked.ShouldNotBeNull()().ShouldBeTrue();
    }

    [Fact]
    public async Task SwitchAsync_Transcribe_SavesTaskWithLanguagesUnchanged()
    {
        // Arrange
        var store = new FakeSettingsStore(new AppSettings());
        var sut = CreateSut(store);
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await sut.SwitchAsync(TranscriptionTask.Transcribe);

        // Assert
        var saved = store.Saves.ShouldHaveSingleItem();
        saved.ShouldBe(new AppSettings {Transcription = new TranscriptionSettings(Task: TranscriptionTask.Transcribe)});
        _items[0].IsChecked.ShouldNotBeNull()().ShouldBeTrue();
        _items[1].IsChecked.ShouldNotBeNull()().ShouldBeFalse();
    }

    [Fact]
    public async Task OnClick_TranscribeItem_SavesTranscribe()
    {
        // Arrange
        var store = new FakeSettingsStore(new AppSettings());
        var sut = CreateSut(store);
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        _items[0].OnClick();

        // Assert
        store.Current.Transcription.Task.ShouldBe(TranscriptionTask.Transcribe);
    }

    [Fact]
    public async Task SwitchAsync_TaskInEffect_DoesNotSave()
    {
        // Arrange
        var store = new FakeSettingsStore(new AppSettings());
        var sut = CreateSut(store);

        // Act
        await sut.SwitchAsync(TranscriptionTask.Translate);

        // Assert
        store.Saves.ShouldBeEmpty();
    }

    [Fact]
    public async Task SwitchAsync_SaveFails_NotifiesAndKeepsTaskInEffectChecked()
    {
        // Arrange
        var store = new FakeSettingsStore(new AppSettings()) {SaveException = new IOException("Disk full")};
        var sut = CreateSut(store);
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await sut.SwitchAsync(TranscriptionTask.Transcribe);

        // Assert
        A.CallTo(() => _notifier.Show(DictationMessages.TaskSwitchFailedTitle,
            DictationMessages.TaskSwitchFailedMessage)).MustHaveHappenedOnceExactly();
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning);
        store.Current.Transcription.Task.ShouldBe(TranscriptionTask.Translate);
        _items[1].IsChecked.ShouldNotBeNull()().ShouldBeTrue();
    }

    [Fact]
    public async Task SwitchAsync_CancelledByShutdown_DoesNotNotify()
    {
        // Arrange
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();
        A.CallTo(() => _lifetime.ApplicationStopping).Returns(stopping.Token);
        var store = new FakeSettingsStore(new AppSettings())
        {
            SaveException = new OperationCanceledException(stopping.Token),
        };
        var sut = CreateSut(store);

        // Act
        await sut.SwitchAsync(TranscriptionTask.Transcribe);

        // Assert
        A.CallTo(() => _notifier.Show(A<string>._, A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task StartAsync_SettingsChangeAfterStart_HeadersFollowSettings()
    {
        // Arrange
        var store = new FakeSettingsStore(new AppSettings());
        var sut = CreateSut(store);
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await store.SaveAsync(new AppSettings
        {
            Transcription = new TranscriptionSettings(SourceLanguage: "en", TargetLanguage: "fr"),
        }, TestContext.Current.CancellationToken);

        // Assert
        _items[0].Header().ShouldBe("Transcribe (English)");
        _items[1].Header().ShouldBe("Translate (English → French)");
    }

    [Theory]
    [InlineData("de", "German")]
    [InlineData("en", "English")]
    [InlineData("pt", "Portuguese")]
    public void LanguageName_KnownCode_ReturnsEnglishName(string code, string expected)
    {
        // Act
        var name = DictationMessages.LanguageName(code);

        // Assert
        name.ShouldBe(expected);
    }

    [Theory]
    [InlineData("zz")]
    [InlineData("not a language")]
    public void LanguageName_UnknownCode_ReturnsCode(string code)
    {
        // Act
        var name = DictationMessages.LanguageName(code);

        // Assert
        name.ShouldBe(code);
    }

    private TaskMenu CreateSut(ISettingsStore settingsStore)
    {
        return new TaskMenu(_trayIcon, settingsStore, _notifier, new InlineUiDispatcher(), _lifetime, _logger);
    }

    private sealed record MenuItem(Func<string> Header, Action OnClick, Func<bool>? IsChecked);
}
