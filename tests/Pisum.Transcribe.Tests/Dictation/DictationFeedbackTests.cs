using System.Drawing;
using Microsoft.Extensions.Time.Testing;
using Pisum.Transcribe.Dictation;
using Pisum.Transcribe.TextInsertion;
using Pisum.Transcribe.Transcription;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.Dictation;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class DictationFeedbackTests
{
    private static readonly DictationIcons Icons = new();
    private static readonly InsertionTarget Target = new(0x1234, 42, false);

    private readonly ITrayIconService _trayIcon = A.Fake<ITrayIconService>();
    private readonly ITranscriber _transcriber = A.Fake<ITranscriber>();
    private readonly IRecordingOverlay _overlay = A.Fake<IRecordingOverlay>();
    private readonly FakeTimeProvider _time = new();
    private readonly List<(Icon Icon, string ToolTip)> _statuses = [];
    private readonly DictationFeedback _sut;

    public DictationFeedbackTests()
    {
        A.CallTo(() => _transcriber.Status).Returns(TranscriberStatus.Ready);
        A.CallTo(() => _transcriber.ActiveBackend).Returns("Vulkan");
        A.CallTo(() => _trayIcon.SetStatus(A<Icon>._, A<string>._))
            .Invokes((Icon icon, string toolTip) => _statuses.Add((icon, toolTip)));
        _sut = new DictationFeedback(_trayIcon, _transcriber, Icons, _time, () => _overlay, action => action());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private (Icon Icon, string ToolTip) Status => _statuses[^1];

    [Fact]
    public async Task StartAsync_EngineReady_ShowsReadyIconWithBackend()
    {
        // Act
        await _sut.StartAsync(Ct);

        // Assert
        Status.ShouldBe((Icons.Ready, "Pisum Transcribe – Ready (Vulkan)"));
    }

    [Fact]
    public async Task StartAsync_EngineAlreadyLoading_ShowsUnavailableWithLoadingToolTip()
    {
        // Arrange
        A.CallTo(() => _transcriber.Status).Returns(TranscriberStatus.Loading);
        A.CallTo(() => _transcriber.ActiveBackend).Returns(null);

        // Act
        await _sut.StartAsync(Ct);

        // Assert
        Status.ShouldBe((Icons.Unavailable, "Pisum Transcribe – Loading model…"));
    }

    [Fact]
    public async Task StartAsync_NoModel_ShowsUnavailableWithNoModelToolTip()
    {
        // Arrange
        A.CallTo(() => _transcriber.Status).Returns(TranscriberStatus.NotLoaded);
        A.CallTo(() => _transcriber.ActiveBackend).Returns(null);

        // Act
        await _sut.StartAsync(Ct);

        // Assert
        Status.ShouldBe((Icons.Unavailable, "Pisum Transcribe – No model installed"));
    }

    [Fact]
    public async Task ShowStarting_EngineReady_ShowsOverlayOnTargetAndKeepsTrayReady()
    {
        // Arrange
        await _sut.StartAsync(Ct);

        // Act
        _sut.ShowStarting(Target);

        // Assert
        A.CallTo(() => _overlay.ShowStarting(Target.WindowHandle)).MustHaveHappenedOnceExactly();
        Status.ShouldBe((Icons.Ready, "Pisum Transcribe – Ready (Vulkan)"));
    }

    [Fact]
    public async Task ShowRecording_Always_ShowsRecordingIconAndOverlay()
    {
        // Arrange
        await _sut.StartAsync(Ct);
        _sut.ShowStarting(Target);

        // Act
        _sut.ShowRecording();

        // Assert
        Status.ShouldBe((Icons.Recording, "Pisum Transcribe – Recording…"));
        A.CallTo(() => _overlay.ShowRecording()).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ShowTranscribing_Always_ShowsTranscribingIconAndOverlay()
    {
        // Arrange
        await _sut.StartAsync(Ct);
        _sut.ShowStarting(Target);
        _sut.ShowRecording();

        // Act
        _sut.ShowTranscribing();

        // Assert
        Status.ShouldBe((Icons.Transcribing, "Pisum Transcribe – Transcribing…"));
        A.CallTo(() => _overlay.ShowTranscribing()).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ShowIdle_AfterDictation_ShowsReadyAgainAndHidesOverlay()
    {
        // Arrange
        A.CallTo(() => _transcriber.ActiveBackend).Returns("CPU");
        await _sut.StartAsync(Ct);
        _sut.ShowStarting(Target);
        _sut.ShowRecording();
        var duringRecording = Status.ToolTip;
        _sut.ShowTranscribing();

        // Act
        _sut.ShowIdle();

        // Assert
        duringRecording.ShouldNotContain("Ready (CPU)");
        Status.ShouldBe((Icons.Ready, "Pisum Transcribe – Ready (CPU)"));
        A.CallTo(() => _overlay.Hide()).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ShowIdle_EngineReloadedDuringDictation_ShowsLoadingThenReadyOnCpu()
    {
        // Arrange
        await _sut.StartAsync(Ct);
        _sut.ShowStarting(Target);
        _sut.ShowRecording();
        _sut.ShowTranscribing();
        A.CallTo(() => _transcriber.ActiveBackend).Returns(null);
        _transcriber.StatusChanged += Raise.With(_transcriber, TranscriberStatus.Loading);
        var duringDictation = Status;

        // Act
        _sut.ShowIdle();
        var afterDictation = Status;
        A.CallTo(() => _transcriber.ActiveBackend).Returns("CPU");
        _transcriber.StatusChanged += Raise.With(_transcriber, TranscriberStatus.Ready);

        // Assert
        duringDictation.ShouldBe((Icons.Transcribing, "Pisum Transcribe – Transcribing…"));
        afterDictation.ShouldBe((Icons.Unavailable, "Pisum Transcribe – Loading model…"));
        Status.ShouldBe((Icons.Ready, "Pisum Transcribe – Ready (CPU)"));
    }

    [Fact]
    public async Task ShowIdle_EngineReadyAgainDuringDictation_StaysTranscribingThenShowsReadyOnCpu()
    {
        // Arrange
        await _sut.StartAsync(Ct);
        _sut.ShowStarting(Target);
        _sut.ShowRecording();
        _sut.ShowTranscribing();
        A.CallTo(() => _transcriber.ActiveBackend).Returns(null);
        _transcriber.StatusChanged += Raise.With(_transcriber, TranscriberStatus.Loading);
        var whileLoading = Status;
        A.CallTo(() => _transcriber.ActiveBackend).Returns("CPU");
        _transcriber.StatusChanged += Raise.With(_transcriber, TranscriberStatus.Ready);
        var whileReady = Status;

        // Act
        _sut.ShowIdle();

        // Assert
        whileLoading.ShouldBe((Icons.Transcribing, "Pisum Transcribe – Transcribing…"));
        whileReady.ShouldBe((Icons.Transcribing, "Pisum Transcribe – Transcribing…"));
        Status.ShouldBe((Icons.Ready, "Pisum Transcribe – Ready (CPU)"));
    }

    [Fact]
    public async Task StatusChanged_Loading_ShowsUnavailableWithLoadingToolTip()
    {
        // Arrange
        await _sut.StartAsync(Ct);

        // Act
        _transcriber.StatusChanged += Raise.With(_transcriber, TranscriberStatus.Loading);

        // Assert
        Status.ShouldBe((Icons.Unavailable, "Pisum Transcribe – Loading model…"));
        A.CallTo(() => _trayIcon.ShowNotification(A<string>._, A<string>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData("CPU", "Pisum Transcribe – Ready (CPU)")]
    [InlineData("Vulkan", "Pisum Transcribe – Ready (Vulkan)")]
    public async Task StatusChanged_Ready_ShowsReadyWithBackend(string backend, string expectedToolTip)
    {
        // Arrange
        A.CallTo(() => _transcriber.Status).Returns(TranscriberStatus.Loading);
        await _sut.StartAsync(Ct);
        A.CallTo(() => _transcriber.ActiveBackend).Returns(backend);

        // Act
        _transcriber.StatusChanged += Raise.With(_transcriber, TranscriberStatus.Ready);

        // Assert
        Status.ShouldBe((Icons.Ready, expectedToolTip));
        A.CallTo(() => _trayIcon.ShowNotification(A<string>._, A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task StatusChanged_Failed_ShowsUnavailableWithFailedToolTip()
    {
        // Arrange
        await _sut.StartAsync(Ct);

        // Act
        _transcriber.StatusChanged += Raise.With(_transcriber, TranscriberStatus.Failed);

        // Assert
        Status.ShouldBe((Icons.Unavailable, "Pisum Transcribe – Model failed to load"));
    }

    [Fact]
    public async Task StatusChanged_AnyState_EveryToolTipContainsProductName()
    {
        // Arrange
        A.CallTo(() => _transcriber.Status).Returns(TranscriberStatus.NotLoaded);
        await _sut.StartAsync(Ct);

        // Act
        _transcriber.StatusChanged += Raise.With(_transcriber, TranscriberStatus.Loading);
        _transcriber.StatusChanged += Raise.With(_transcriber, TranscriberStatus.Failed);
        _transcriber.StatusChanged += Raise.With(_transcriber, TranscriberStatus.Ready);
        _sut.ShowStarting(Target);
        _sut.ShowRecording();
        _sut.ShowTranscribing();
        _sut.ShowIdle();

        // Assert
        _statuses.Select(status => status.Icon).Distinct().Count().ShouldBe(4);
        _statuses.ShouldAllBe(status => status.ToolTip.Contains("Pisum Transcribe"));
    }

    [Fact]
    public async Task ShowNoSpeech_ThenIdle_HidesOverlayAfterMessageDuration()
    {
        // Arrange
        await _sut.StartAsync(Ct);
        _sut.ShowStarting(Target);
        _sut.ShowRecording();
        _sut.ShowTranscribing();

        // Act
        _sut.ShowNoSpeech();
        _sut.ShowIdle();
        var hiddenAtOnce = Fake.GetCalls(_overlay).Any(call => call.Method.Name == nameof(IRecordingOverlay.Hide));
        _time.Advance(DictationFeedback.MessageDuration);

        // Assert
        A.CallTo(() => _overlay.ShowMessage("No speech detected")).MustHaveHappenedOnceExactly();
        hiddenAtOnce.ShouldBeFalse();
        A.CallTo(() => _overlay.Hide()).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ShowBusy_WhileTranscribing_ReturnsToTranscribingAfterMessageDuration()
    {
        // Arrange
        await _sut.StartAsync(Ct);
        _sut.ShowStarting(Target);
        _sut.ShowRecording();
        _sut.ShowTranscribing();

        // Act
        _sut.ShowBusy();
        _time.Advance(DictationFeedback.MessageDuration);

        // Assert
        A.CallTo(() => _overlay.ShowTranscribing()).MustHaveHappenedTwiceExactly();
        A.CallTo(() => _overlay.ShowMessage("Still processing…")).MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _overlay.ShowTranscribing()).MustHaveHappened());
        A.CallTo(() => _overlay.Hide()).MustNotHaveHappened();
    }

    [Fact]
    public async Task ShowStarting_WhileMessageShown_EndsMessageWithoutLaterHide()
    {
        // Arrange
        await _sut.StartAsync(Ct);
        _sut.ShowTranscribing();
        _sut.ShowNoSpeech();
        _sut.ShowIdle();

        // Act
        _sut.ShowStarting(Target);
        _time.Advance(DictationFeedback.MessageDuration);

        // Assert
        A.CallTo(() => _overlay.Hide()).MustNotHaveHappened();
    }

    [Fact]
    public async Task StartAsync_AddsCancelMenuItem_VisibleOnlyWhileTranscribing()
    {
        // Act
        var (_, isVisible) = await StartWithCancelMenuItemAsync();
        var whileIdle = isVisible();
        _sut.ShowStarting(Target);
        _sut.ShowRecording();
        var whileRecording = isVisible();
        _sut.ShowTranscribing();
        var whileTranscribing = isVisible();
        _sut.ShowIdle();
        var afterDictation = isVisible();

        // Assert
        whileIdle.ShouldBeFalse();
        whileRecording.ShouldBeFalse();
        whileTranscribing.ShouldBeTrue();
        afterDictation.ShouldBeFalse();
    }

    [Fact]
    public async Task CancelMenuItem_Chosen_RaisesCancelRequested()
    {
        // Arrange
        var (onClick, _) = await StartWithCancelMenuItemAsync();
        var raised = 0;
        _sut.CancelRequested += (_, _) => raised++;

        // Act
        onClick();

        // Assert
        raised.ShouldBe(1);
    }

    [Fact]
    public void Notify_Always_ShowsTrayNotification()
    {
        // Act
        _sut.Notify("Recording failed", "The microphone is muted.");

        // Assert
        A.CallTo(() => _trayIcon.ShowNotification("Recording failed", "The microphone is muted."))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task StopAsync_WhileRecording_HidesOverlayWithoutTrayUpdate()
    {
        // Arrange
        await _sut.StartAsync(Ct);
        _sut.ShowStarting(Target);
        _sut.ShowRecording();
        var statuses = _statuses.Count;

        // Act
        await _sut.StopAsync(Ct);

        // Assert
        A.CallTo(() => _overlay.Hide()).MustHaveHappenedOnceExactly();
        _statuses.Count.ShouldBe(statuses);
    }

    [Fact]
    public async Task StopAsync_WhileTranscribing_HidesOverlay()
    {
        // Arrange
        await _sut.StartAsync(Ct);
        _sut.ShowStarting(Target);
        _sut.ShowRecording();
        _sut.ShowTranscribing();
        var statuses = _statuses.Count;

        // Act
        await _sut.StopAsync(Ct);

        // Assert
        A.CallTo(() => _overlay.Hide()).MustHaveHappenedOnceExactly();
        _statuses.Count.ShouldBe(statuses);
    }

    [Fact]
    public async Task StopAsync_WhileMessageShown_HidesOverlayAndIgnoresMessageTimer()
    {
        // Arrange
        await _sut.StartAsync(Ct);
        _sut.ShowTranscribing();
        _sut.ShowBusy();

        // Act
        await _sut.StopAsync(Ct);
        _time.Advance(DictationFeedback.MessageDuration);

        // Assert
        A.CallTo(() => _overlay.Hide()).MustHaveHappenedOnceExactly();
        A.CallTo(() => _overlay.ShowTranscribing()).MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// Starts the feedback and returns the callbacks of its <b>Cancel transcription</b> menu item.
    /// </summary>
    private async Task<(Action OnClick, Func<bool> IsVisible)> StartWithCancelMenuItemAsync()
    {
        Action? onClick = null;
        Func<bool>? isVisible = null;
        A.CallTo(() => _trayIcon.AddMenuItem("Cancel transcription", A<Action>._, A<Func<bool>?>._))
            .Invokes((string _, Action click, Func<bool>? visible) =>
            {
                onClick = click;
                isVisible = visible;
            });
        await _sut.StartAsync(Ct);
        return (onClick.ShouldNotBeNull(), isVisible.ShouldNotBeNull());
    }
}
