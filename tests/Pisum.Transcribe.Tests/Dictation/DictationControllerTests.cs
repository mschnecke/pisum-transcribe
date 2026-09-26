using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Pisum.Transcribe.Dictation;
using Pisum.Transcribe.Tests.Hosting;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.TextInsertion;
using Pisum.Transcribe.Transcription;
using Pisum.Transcribe.VoiceActivity;

namespace Pisum.Transcribe.Tests.Dictation;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class DictationControllerTests : IAsyncLifetime
{
    private const string Transcript = "Grüße aus Köln.";

    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxInputDuration = TimeSpan.FromSeconds(123);
    private static readonly TimeSpan HoldDuration = TimeSpan.FromSeconds(1);
    private static readonly InsertionTarget Target = new(0x1234, 42, false);
    private static readonly AudioClip Clip = new(new float[AudioClip.SampleRate]);

    // Three seconds of distinct samples, with speech detected in the middle second.
    private static readonly AudioClip SpeechClip =
        new(Enumerable.Range(0, 3 * AudioClip.SampleRate).Select(index => (float) index).ToArray());

    private static readonly SpeechSegment MiddleSecond = new(AudioClip.SampleRate, 2 * AudioClip.SampleRate);

    private static readonly float[] TrimmedSpeechClip =
        SpeechClip.Samples[(MiddleSecond.StartSample - AudioTrimmer.DefaultPadSamples)..
            (MiddleSecond.EndSample + AudioTrimmer.DefaultPadSamples)];

    private static readonly TranscriptionOptions PressOptions = new(TranscriptionTask.Translate, "de", "en");
    private static readonly TextInsertionSettings PressTextInsertion = new(InsertionMethod.ClipboardPaste, true);

    private static readonly AppSettings PressSettings = new()
    {
        Transcription = new TranscriptionSettings(BackendPreference.Auto, TranscriptionTask.Translate, "de", "en"),
        TextInsertion = PressTextInsertion,
    };

    private readonly IPushToTalkHotkey _hotkey = A.Fake<IPushToTalkHotkey>();
    private readonly IAudioRecorder _recorder = A.Fake<IAudioRecorder>();
    private readonly ITranscriber _transcriber = A.Fake<ITranscriber>();
    private readonly FakeVoiceActivityDetector _detector = new();
    private readonly IForegroundWindowTracker _tracker = A.Fake<IForegroundWindowTracker>();
    private readonly ITextInserter _inserter = A.Fake<ITextInserter>();
    private readonly ISettingsStore _settingsStore = A.Fake<ISettingsStore>();
    private readonly FakeDictationFeedback _feedback = new();
    private readonly DictationState _dictationState = new();
    private readonly RecordingProcessActivity _processActivity = new();
    private readonly FakeTimeProvider _time = new();
    private readonly CapturingLogger<DictationController> _logger = new();
    private readonly DictationController _sut;

    public DictationControllerTests()
    {
        A.CallTo(() => _transcriber.Status).Returns(TranscriberStatus.Ready);
        A.CallTo(() => _transcriber.MaxInputDuration).Returns(MaxInputDuration);
        A.CallTo(() => _transcriber.TranscribeAsync(A<float[]>._, A<TranscriptionOptions>._, A<CancellationToken>._))
            .Returns(Result(Transcript));
        A.CallTo(() => _tracker.CaptureForeground()).Returns(Target);
        A.CallTo(() => _settingsStore.Current).Returns(PressSettings);
        A.CallTo(() => _recorder.StopAsync()).Returns(Clip);
        A.CallTo(() => _inserter.InsertAsync(A<string>._, A<InsertionTarget>._, A<TextInsertionSettings>._,
                A<CancellationToken>._))
            .Returns(InsertionOutcome.Inserted);
        _sut = new DictationController(_hotkey, _recorder, _transcriber, _detector, _tracker, _inserter, _settingsStore,
            _feedback, _dictationState, _processActivity, _time, _logger);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _sut.StartAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _sut.StopAsync(CancellationToken.None).WaitAsync(SignalTimeout, CancellationToken.None);
    }

    [Fact]
    public async Task Pressed_EngineReady_ShowsStartingThenStartsRecorderThenShowsRecordingOnceAudioFlows()
    {
        // Arrange
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => _recorder.StartAsync(A<TimeSpan>._, A<CancellationToken>._)).Returns(start.Task);

        // Act
        Press();
        await WaitUntilAsync(() => StartCalls() == 1);
        var callsBeforeAudio = _feedback.Calls;
        start.SetResult();
        await WaitForAsync(FakeDictationFeedback.Recording);

        // Assert
        callsBeforeAudio.ShouldBe([FakeDictationFeedback.Starting]);
        _feedback.StartingTargets.ShouldBe([Target]);
        A.CallTo(() => _recorder.StartAsync(MaxInputDuration, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        _feedback.Calls.ShouldBe([FakeDictationFeedback.Starting, FakeDictationFeedback.Recording]);
    }

    [Fact]
    public async Task Pressed_StartCompletes_LogsTimeFromPressAtInformation()
    {
        // Arrange
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => _recorder.StartAsync(A<TimeSpan>._, A<CancellationToken>._)).Returns(start.Task);

        // Act
        Press();
        await WaitUntilAsync(() => StartCalls() == 1);
        _time.Advance(TimeSpan.FromMilliseconds(150));
        start.SetResult();
        await WaitForAsync(FakeDictationFeedback.Recording);

        // Assert
        _logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Information && entry.Message == "Recording started 150 ms after the press");
    }

    [Fact]
    public async Task Released_AfterOneSecond_StopsTranscribesInsertsAndShowsIdle()
    {
        // Act
        await DictateAsync(HoldDuration);

        // Assert
        A.CallTo(() => _recorder.StopAsync()).MustHaveHappenedOnceExactly();
        A.CallTo(() => _transcriber.TranscribeAsync(Clip.Samples, PressOptions, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _inserter.InsertAsync(Transcript, Target, PressTextInsertion, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _recorder.AbortAsync()).MustNotHaveHappened();
        _feedback.Calls.ShouldBe([
            FakeDictationFeedback.Starting, FakeDictationFeedback.Recording, FakeDictationFeedback.Transcribing,
            FakeDictationFeedback.Idle,
        ]);
        _feedback.Notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task Released_Within200Ms_AbortsWithoutTranscribing()
    {
        // Act
        await DictateAsync(TimeSpan.FromMilliseconds(200));

        // Assert
        A.CallTo(() => _recorder.AbortAsync()).MustHaveHappenedOnceExactly();
        A.CallTo(() => _recorder.StopAsync()).MustNotHaveHappened();
        ShouldNotHaveTranscribed();
        _feedback.Calls.ShouldBe([
            FakeDictationFeedback.Starting, FakeDictationFeedback.Recording, FakeDictationFeedback.Idle,
        ]);
        _feedback.Notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task Released_WhileStartPending_AbortsOnceStartCompletesWithoutTranscribing()
    {
        // Arrange
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => _recorder.StartAsync(A<TimeSpan>._, A<CancellationToken>._)).Returns(start.Task);
        Press();
        await WaitUntilAsync(() => StartCalls() == 1);
        _time.Advance(HoldDuration);

        // Act
        Release();
        start.SetResult();
        await WaitForAsync(FakeDictationFeedback.Idle);

        // Assert
        A.CallTo(() => _recorder.AbortAsync()).MustHaveHappenedOnceExactly();
        A.CallTo(() => _recorder.StopAsync()).MustNotHaveHappened();
        ShouldNotHaveTranscribed();
        _feedback.Notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancelled_DuringRecording_AbortsWithoutInserting()
    {
        // Arrange
        await RecordAsync();
        _time.Advance(HoldDuration);

        // Act
        _hotkey.Cancelled += Raise.WithEmpty();
        await WaitForAsync(FakeDictationFeedback.Idle);

        // Assert
        A.CallTo(() => _recorder.AbortAsync()).MustHaveHappenedOnceExactly();
        A.CallTo(() => _recorder.StopAsync()).MustNotHaveHappened();
        ShouldNotHaveTranscribed();
        ShouldNotHaveInserted();
        _feedback.Notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task Released_SettingsSavedDuringRecording_UsesSettingsFromPress()
    {
        // Arrange
        await RecordAsync();
        A.CallTo(() => _settingsStore.Current).Returns(new AppSettings
        {
            Transcription = new TranscriptionSettings(BackendPreference.Cpu, TranscriptionTask.Transcribe, "en", "de"),
            TextInsertion = new TextInsertionSettings(InsertionMethod.TypeText, false),
        });
        _time.Advance(HoldDuration);

        // Act
        Release();
        await WaitForAsync(FakeDictationFeedback.Idle);

        // Assert
        A.CallTo(() => _transcriber.TranscribeAsync(A<float[]>._, PressOptions, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _inserter.InsertAsync(Transcript, Target, PressTextInsertion, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Released_VoiceActivityDisabled_TranscribesFullRecordingWithoutDetection()
    {
        // Arrange
        A.CallTo(() => _settingsStore.Current)
            .Returns(PressSettings with {VoiceActivity = new VoiceActivitySettings(false)});
        _detector.Detect = (_, _) => [];

        // Act
        await DictateAsync(HoldDuration);

        // Assert
        _detector.Calls.ShouldBe(0);
        A.CallTo(() => _transcriber.TranscribeAsync(Clip.Samples, PressOptions, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _inserter.InsertAsync(Transcript, Target, PressTextInsertion, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Released_NoSpeechDetected_ShowsNoSpeechWithoutTranscribing()
    {
        // Arrange
        _detector.Detect = (_, _) => [];

        // Act
        await DictateAsync(HoldDuration);

        // Assert
        ShouldNotHaveTranscribed();
        ShouldNotHaveInserted();
        _feedback.Calls.ShouldBe([
            FakeDictationFeedback.Starting, FakeDictationFeedback.Recording, FakeDictationFeedback.Transcribing,
            FakeDictationFeedback.NoSpeech, FakeDictationFeedback.Idle,
        ]);
        _feedback.Notifications.ShouldBeEmpty();
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Information &&
                                               entry.Message ==
                                               "Voice activity detection found no speech in 1.00 s of audio in 0 ms");
    }

    [Fact]
    public async Task Released_SpeechDetected_TranscribesTrimmedRecordingAndLogsDurations()
    {
        // Arrange
        A.CallTo(() => _recorder.StopAsync()).Returns(SpeechClip);
        _detector.Detect = (_, _) => [MiddleSecond];

        // Act
        await DictateAsync(HoldDuration);

        // Assert
        A.CallTo(() => _transcriber.TranscribeAsync(A<float[]>.That.IsSameSequenceAs(TrimmedSpeechClip), PressOptions,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _inserter.InsertAsync(Transcript, Target, PressTextInsertion, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        _logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Information &&
            entry.Message == "Voice activity detection trimmed 3.00 s of audio to 1.60 s in 0 ms");
    }

    [Fact]
    public async Task Released_DetectorThrows_TranscribesFullRecordingAndLogsWarningWithoutNotifying()
    {
        // Arrange
        _detector.Detect = (_, _) => throw new InvalidOperationException("The model failed to load.");

        // Act
        await DictateAsync(HoldDuration);

        // Assert
        A.CallTo(() => _transcriber.TranscribeAsync(Clip.Samples, PressOptions, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _inserter.InsertAsync(Transcript, Target, PressTextInsertion, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        _logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning && entry.Exception is InvalidOperationException);
        _feedback.Notifications.ShouldBeEmpty();
        _feedback.Calls[^1].ShouldBe(FakeDictationFeedback.Idle);
    }

    [Fact]
    public async Task Released_VoiceActivitySavedAsDisabledDuringRecording_TrimsThisDictationOnly()
    {
        // Arrange
        A.CallTo(() => _recorder.StopAsync()).Returns(SpeechClip);
        _detector.Detect = (_, _) => [MiddleSecond];
        await RecordAsync();
        A.CallTo(() => _settingsStore.Current)
            .Returns(PressSettings with {VoiceActivity = new VoiceActivitySettings(false)});
        _time.Advance(HoldDuration);

        // Act
        Release();
        await WaitForAsync(FakeDictationFeedback.Idle);
        await RecordAsync();
        _time.Advance(HoldDuration);
        Release();
        await WaitForAsync(FakeDictationFeedback.Idle, 2);

        // Assert
        _detector.Calls.ShouldBe(1);
        A.CallTo(() => _transcriber.TranscribeAsync(A<float[]>.That.IsSameSequenceAs(TrimmedSpeechClip), PressOptions,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _transcriber.TranscribeAsync(SpeechClip.Samples, PressOptions, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly());
    }

    [Fact]
    public async Task StopAsync_WhileIdle_DoesNotAbortRecording()
    {
        // Act
        await _sut.StopAsync(Ct).WaitAsync(SignalTimeout, Ct);

        // Assert
        A.CallTo(() => _recorder.AbortAsync()).MustNotHaveHappened();
    }

    [Fact]
    public async Task StopAsync_WhileStartPending_CancelsStartAndStops()
    {
        // Arrange
        var startToken = CancellationToken.None;
        A.CallTo(() => _recorder.StartAsync(A<TimeSpan>._, A<CancellationToken>._))
            .ReturnsLazily((TimeSpan _, CancellationToken token) =>
            {
                startToken = token;
                return Task.Delay(Timeout.Infinite, token);
            });
        Press();
        await WaitUntilAsync(() => StartCalls() == 1);

        // Act
        await _sut.StopAsync(Ct).WaitAsync(SignalTimeout, Ct);

        // Assert
        startToken.IsCancellationRequested.ShouldBeTrue();
        A.CallTo(() => _recorder.AbortAsync()).MustNotHaveHappened();
        _feedback.Calls.ShouldNotContain(FakeDictationFeedback.Recording);
        _feedback.Notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task StopAsync_WhileRecording_AbortsRecording()
    {
        // Arrange
        await RecordAsync();
        _time.Advance(HoldDuration);

        // Act
        await _sut.StopAsync(Ct).WaitAsync(SignalTimeout, Ct);

        // Assert
        A.CallTo(() => _recorder.AbortAsync()).MustHaveHappenedOnceExactly();
        A.CallTo(() => _recorder.StopAsync()).MustNotHaveHappened();
        ShouldNotHaveTranscribed();
        _feedback.Calls.ShouldBe([FakeDictationFeedback.Starting, FakeDictationFeedback.Recording]);
        _feedback.Notifications.ShouldBeEmpty();
        _logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Information && entry.Message == "The recording was aborted at exit after 1.00 s");
    }

    [Fact]
    public async Task StopAsync_DuringSpeechDetection_CancelsDetectionWithoutTranscribing()
    {
        // Arrange
        using var detecting = new ManualResetEventSlim();
        var detectionToken = CancellationToken.None;
        _detector.Detect = (_, token) =>
        {
            detectionToken = token;
            detecting.Set();
            token.WaitHandle.WaitOne(SignalTimeout);
            token.ThrowIfCancellationRequested();
            return [];
        };
        await RecordAsync();
        _time.Advance(HoldDuration);
        Release();
        await WaitUntilAsync(() => detecting.IsSet);

        // Act
        await _sut.StopAsync(Ct).WaitAsync(SignalTimeout, Ct);

        // Assert
        detectionToken.IsCancellationRequested.ShouldBeTrue();
        A.CallTo(() => _recorder.AbortAsync()).MustNotHaveHappened();
        ShouldNotHaveTranscribed();
        _feedback.Calls.ShouldNotContain(FakeDictationFeedback.NoSpeech);
        _feedback.Notifications.ShouldBeEmpty();
        _logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Released_TranscriptWithSurroundingWhitespace_InsertsTrimmedText()
    {
        // Arrange
        A.CallTo(() => _transcriber.TranscribeAsync(A<float[]>._, A<TranscriptionOptions>._, A<CancellationToken>._))
            .Returns(Result($"  {Transcript} \n"));

        // Act
        await DictateAsync(HoldDuration);

        // Assert
        A.CallTo(() => _inserter.InsertAsync(Transcript, Target, PressTextInsertion, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Released_EmptyTranscript_ShowsNoSpeechWithoutInserting()
    {
        // Arrange
        A.CallTo(() => _transcriber.TranscribeAsync(A<float[]>._, A<TranscriptionOptions>._, A<CancellationToken>._))
            .Returns(Result(" \n "));

        // Act
        await DictateAsync(HoldDuration);

        // Assert
        ShouldNotHaveInserted();
        _feedback.Calls.ShouldBe([
            FakeDictationFeedback.Starting, FakeDictationFeedback.Recording, FakeDictationFeedback.Transcribing,
            FakeDictationFeedback.NoSpeech, FakeDictationFeedback.Idle,
        ]);
        _feedback.Notifications.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(nameof(InsertionOutcome.TargetWindowChanged), "the active window changed")]
    [InlineData(nameof(InsertionOutcome.TargetWindowElevated), "the target window runs as administrator")]
    [InlineData(nameof(InsertionOutcome.ModifierKeysHeld), "modifier keys were held")]
    [InlineData(nameof(InsertionOutcome.SecureInputOn), "secure input is on, for example in a password field")]
    [InlineData(nameof(InsertionOutcome.KeystrokesNotAllowed), "Accessibility access isn't in effect")]
    public async Task Released_InsertionFallsBack_NotifiesTextCopiedWithReason(string outcome, string reason)
    {
        // Arrange
        ReturnInsertionOutcome(Enum.Parse<InsertionOutcome>(outcome));

        // Act
        await DictateAsync(HoldDuration);

        // Assert
        var notification = _feedback.Notifications.ShouldHaveSingleItem();
        notification.Title.ShouldBe("Text copied to the clipboard");
        notification.Message.ShouldContain(reason);
        notification.Message.ShouldEndWith(OperatingSystem.IsMacOS() ? "Paste it with Command+V." : "Paste it with Ctrl+V.");
        _feedback.Calls[^1].ShouldBe(FakeDictationFeedback.Idle);
    }

    [Fact]
    public async Task Released_ClipboardUnavailable_NotifiesTextNotDeliveredWithoutClaimingCopy()
    {
        // Arrange
        ReturnInsertionOutcome(InsertionOutcome.ClipboardUnavailable);

        // Act
        await DictateAsync(HoldDuration);

        // Assert
        var notification = _feedback.Notifications.ShouldHaveSingleItem();
        notification.Title.ShouldNotBe(DictationMessages.CopiedTitle);
        notification.Message.ShouldContain("could not be inserted or copied");
        notification.Message.ShouldContain("another application is using the clipboard");
        notification.Message.ShouldNotContain("Paste");
    }

    [Fact]
    public async Task Released_LanguageNotSupported_NotifiesItsMessageWithoutInserting()
    {
        // Arrange
        var exception = new LanguageNotSupportedException("Canary 180M Flash does not support the source language \"pl\".");
        A.CallTo(() => _transcriber.TranscribeAsync(A<float[]>._, A<TranscriptionOptions>._, A<CancellationToken>._))
            .ThrowsAsync(exception);

        // Act
        await DictateAsync(HoldDuration);

        // Assert
        _feedback.Notifications.ShouldBe([new Notification(DictationMessages.DictationFailedTitle, exception.Message)]);
        ShouldNotHaveInserted();
        _feedback.Calls[^1].ShouldBe(FakeDictationFeedback.Idle);
    }

    [Fact]
    public async Task Released_UnknownException_NotifiesGenericFailureAndLogsWithoutTranscript()
    {
        // Arrange
        A.CallTo(() => _inserter.InsertAsync(A<string>._, A<InsertionTarget>._, A<TextInsertionSettings>._,
                A<CancellationToken>._))
            .ThrowsAsync(new InvalidOperationException("Unexpected"));

        // Act
        await DictateAsync(HoldDuration);

        // Assert
        _feedback.Notifications.ShouldBe([
            new Notification(DictationMessages.DictationFailedTitle, DictationMessages.DictationFailedMessage),
        ]);
        _logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException);
        _logger.Entries.ShouldAllBe(entry => !entry.Message.Contains("Köln"));
        _feedback.Calls[^1].ShouldBe(FakeDictationFeedback.Idle);
    }

    [Fact]
    public async Task Pressed_RepeatedlyWhileLoading_NotifiesAtMostOnceEvery10Seconds()
    {
        // Arrange
        A.CallTo(() => _transcriber.Status).Returns(TranscriberStatus.Loading);

        // Act: three presses within 5 s.
        Press();
        await WaitUntilAsync(() => LoadingNotifications() == 1);
        _time.Advance(TimeSpan.FromSeconds(2));
        Press();
        _time.Advance(TimeSpan.FromSeconds(2));
        Press();
        await WaitUntilAsync(() => StatusReads() == 3);

        // A press without a model has its own reason, so its notification shows that the presses before were handled.
        A.CallTo(() => _transcriber.Status).Returns(TranscriberStatus.NotLoaded);
        Press();
        await WaitUntilAsync(() => _feedback.Notifications.Count == 2);
        var withinFiveSeconds = LoadingNotifications();

        // Act: a press 11 s after the first.
        A.CallTo(() => _transcriber.Status).Returns(TranscriberStatus.Loading);
        _time.Advance(TimeSpan.FromSeconds(7));
        Press();
        await WaitUntilAsync(() => LoadingNotifications() == 2);

        // Assert
        withinFiveSeconds.ShouldBe(1);
        _feedback.Notifications.ShouldBe([
            new Notification("Dictation not available", DictationMessages.LoadingMessage),
            new Notification("Dictation not available", DictationMessages.NoModelMessage),
            new Notification("Dictation not available", DictationMessages.LoadingMessage),
        ]);
        A.CallTo(() => _recorder.StartAsync(A<TimeSpan>._, A<CancellationToken>._)).MustNotHaveHappened();
        _feedback.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Pressed_NoModel_NotifiesThePlatformsSetupMenuItem()
    {
        // Arrange
        A.CallTo(() => _transcriber.Status).Returns(TranscriberStatus.NotLoaded);

        // Act
        Press();
        await WaitUntilAsync(() => _feedback.Notifications.Count == 1);

        // Assert
        _feedback.Notifications[0].Message.ShouldContain(OperatingSystem.IsMacOS()
            ? "Choose Set up Pisum Transcribe… in the menu bar"
            : "Choose Download model… in the tray menu");
        A.CallTo(() => _recorder.StartAsync(A<TimeSpan>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData(TranscribeCppTranscriber.DamagedModelMessage, TranscribeCppTranscriber.DamagedModelMessage)]
    [InlineData(null, DictationMessages.FailedMessage)]
    public async Task Pressed_WhileFailed_NotifiesFailureMessageOrGenericText(string? failureMessage, string expected)
    {
        // Arrange
        A.CallTo(() => _transcriber.Status).Returns(TranscriberStatus.Failed);
        A.CallTo(() => _transcriber.FailureMessage).Returns(failureMessage);

        // Act
        Press();
        await WaitUntilAsync(() => _feedback.Notifications.Count == 1);

        // Assert
        _feedback.Notifications.ShouldBe([new Notification(DictationMessages.NotReadyTitle, expected)]);
        A.CallTo(() => _recorder.StartAsync(A<TimeSpan>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Pressed_DuringProcessing_ShowsBusyWithoutSecondRecordingAndInsertsFirstDictation()
    {
        // Arrange
        var transcription = new TaskCompletionSource<TranscriptionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => _transcriber.TranscribeAsync(A<float[]>._, A<TranscriptionOptions>._, A<CancellationToken>._))
            .Returns(transcription.Task);
        await RecordAsync();
        _time.Advance(HoldDuration);
        Release();
        await WaitForAsync(FakeDictationFeedback.Transcribing);

        // Act
        Press();
        await WaitForAsync(FakeDictationFeedback.Busy);
        transcription.SetResult(Result(Transcript));
        await WaitForAsync(FakeDictationFeedback.Idle);

        // Assert
        StartCalls().ShouldBe(1);
        A.CallTo(() => _inserter.InsertAsync(Transcript, Target, PressTextInsertion, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        _feedback.Calls.ShouldBe([
            FakeDictationFeedback.Starting, FakeDictationFeedback.Recording, FakeDictationFeedback.Transcribing,
            FakeDictationFeedback.Busy, FakeDictationFeedback.Idle,
        ]);
    }

    [Fact]
    public async Task Pressed_RightAfterInsertReturned_StartsNewRecording()
    {
        // Arrange
        await DictateAsync(HoldDuration);

        // Act
        Press();
        await WaitForAsync(FakeDictationFeedback.Recording, 2);

        // Assert
        StartCalls().ShouldBe(2);
        _feedback.Calls.ShouldNotContain(FakeDictationFeedback.Busy);
    }

    [Fact]
    public async Task MaxDurationReached_DuringRecording_TranscribesInsertsAndNotifies()
    {
        // Arrange
        await RecordAsync();
        _time.Advance(MaxInputDuration);

        // Act
        _recorder.MaxDurationReached += Raise.WithEmpty();
        await WaitForAsync(FakeDictationFeedback.Idle);

        // Assert
        A.CallTo(() => _recorder.StopAsync()).MustHaveHappenedOnceExactly();
        A.CallTo(() => _transcriber.TranscribeAsync(Clip.Samples, PressOptions, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _inserter.InsertAsync(Transcript, Target, PressTextInsertion, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        _feedback.Notifications.ShouldBe([
            new Notification("Maximum dictation length reached", DictationMessages.MaxDurationMessage),
        ]);
    }

    [Theory]
    [InlineData(nameof(MicrophoneAccessDeniedException))]
    [InlineData(nameof(MicrophoneNotRespondingException))]
    public async Task Pressed_StartThrowsRecordingFailed_NotifiesAndNextPressRetries(string exceptionType)
    {
        // Arrange
        RecordingFailedException exception = exceptionType == nameof(MicrophoneAccessDeniedException)
            ? new MicrophoneAccessDeniedException()
            : new MicrophoneNotRespondingException();
        A.CallTo(() => _recorder.StartAsync(A<TimeSpan>._, A<CancellationToken>._)).ThrowsAsync(exception).Once();

        // Act
        Press();
        await WaitForAsync(FakeDictationFeedback.Idle);
        var callsAfterFailure = _feedback.Calls;
        Press();
        await WaitForAsync(FakeDictationFeedback.Recording);

        // Assert
        _feedback.Notifications.ShouldBe([new Notification(DictationMessages.RecordingFailedTitle, exception.Message)]);
        callsAfterFailure.ShouldBe([FakeDictationFeedback.Starting, FakeDictationFeedback.Idle]);
        StartCalls().ShouldBe(2);
    }

    [Fact]
    public async Task RecordingFailed_DuringRecording_NotifiesAndShowsIdleAndNextPressRetries()
    {
        // Arrange
        var exception = new MicrophoneDisconnectedException();
        await RecordAsync();

        // Act
        _recorder.Failed += Raise.With<RecordingFailedException>(_recorder, exception);
        await WaitForAsync(FakeDictationFeedback.Idle);
        Press();
        await WaitForAsync(FakeDictationFeedback.Recording, 2);

        // Assert
        _feedback.Notifications.ShouldBe([new Notification(DictationMessages.RecordingFailedTitle, exception.Message)]);
        A.CallTo(() => _recorder.AbortAsync()).MustNotHaveHappened();
        A.CallTo(() => _recorder.StopAsync()).MustNotHaveHappened();
        ShouldNotHaveTranscribed();
    }

    [Fact]
    public async Task Released_StopThrowsMicrophoneDisconnected_NotifiesWithoutTranscribing()
    {
        // Arrange
        var exception = new MicrophoneDisconnectedException();
        A.CallTo(() => _recorder.StopAsync()).ThrowsAsync(exception);

        // Act
        await DictateAsync(HoldDuration);

        // Assert
        _feedback.Notifications.ShouldBe([new Notification(DictationMessages.RecordingFailedTitle, exception.Message)]);
        ShouldNotHaveTranscribed();
        _feedback.Calls.ShouldBe([
            FakeDictationFeedback.Starting, FakeDictationFeedback.Recording, FakeDictationFeedback.Idle,
        ]);
    }

    [Fact]
    public async Task RecordingFailed_AfterReleaseWasHandled_IsDropped()
    {
        // Arrange
        var transcription = HoldTranscription();
        await RecordAsync();
        _time.Advance(HoldDuration);
        Release();
        await WaitForAsync(FakeDictationFeedback.Transcribing);

        // Act: queued before the processing completes, so it is handled first.
        _recorder.Failed += Raise.With<RecordingFailedException>(_recorder, new MicrophoneDisconnectedException());
        transcription.SetResult(Result(Transcript));
        await WaitForAsync(FakeDictationFeedback.Idle);

        // Assert
        _feedback.Notifications.ShouldBeEmpty();
        A.CallTo(() => _inserter.InsertAsync(Transcript, Target, PressTextInsertion, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task MaxDurationReached_AfterReleaseWasHandled_IsDropped()
    {
        // Arrange
        var transcription = HoldTranscription();
        await RecordAsync();
        _time.Advance(HoldDuration);
        Release();
        await WaitForAsync(FakeDictationFeedback.Transcribing);

        // Act: queued before the processing completes, so it is handled first.
        _recorder.MaxDurationReached += Raise.WithEmpty();
        transcription.SetResult(Result(Transcript));
        await WaitForAsync(FakeDictationFeedback.Idle);

        // Assert
        A.CallTo(() => _recorder.StopAsync()).MustHaveHappenedOnceExactly();
        _feedback.Notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task CancelRequested_DuringTranscription_ShowsIdleWithoutInsertOrNotification()
    {
        // Arrange
        var transcriptionToken = HoldTranscriptionUntilCancelled();
        await RecordAsync();
        _time.Advance(HoldDuration);
        Release();
        await transcriptionToken.WaitAsync(SignalTimeout, Ct);

        // Act
        _feedback.RequestCancel();
        await WaitForAsync(FakeDictationFeedback.Idle);

        // Assert
        (await transcriptionToken).IsCancellationRequested.ShouldBeTrue();
        ShouldNotHaveInserted();
        _feedback.Notifications.ShouldBeEmpty();
        _feedback.Calls.ShouldBe([
            FakeDictationFeedback.Starting, FakeDictationFeedback.Recording, FakeDictationFeedback.Transcribing,
            FakeDictationFeedback.Idle,
        ]);
    }

    [Fact]
    public async Task CancelRequested_DuringSpeechDetection_ShowsIdleWithoutTranscribing()
    {
        // Arrange
        using var detecting = new ManualResetEventSlim();
        var detectionToken = CancellationToken.None;
        _detector.Detect = (_, token) =>
        {
            detectionToken = token;
            detecting.Set();
            token.WaitHandle.WaitOne(SignalTimeout);
            token.ThrowIfCancellationRequested();
            return [];
        };
        await RecordAsync();
        _time.Advance(HoldDuration);
        Release();
        await WaitUntilAsync(() => detecting.IsSet);

        // Act
        _feedback.RequestCancel();
        await WaitForAsync(FakeDictationFeedback.Idle);

        // Assert
        detectionToken.IsCancellationRequested.ShouldBeTrue();
        ShouldNotHaveTranscribed();
        _feedback.Calls.ShouldNotContain(FakeDictationFeedback.NoSpeech);
        _feedback.Notifications.ShouldBeEmpty();
        _logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task CancelRequested_ThenPressed_StartsNewRecordingWithoutBusy()
    {
        // Arrange
        var transcriptionToken = HoldTranscriptionUntilCancelled();
        await RecordAsync();
        _time.Advance(HoldDuration);
        Release();
        await transcriptionToken.WaitAsync(SignalTimeout, Ct);
        _feedback.RequestCancel();
        await WaitForAsync(FakeDictationFeedback.Idle);

        // Act
        Press();
        await WaitForAsync(FakeDictationFeedback.Recording, 2);

        // Assert
        StartCalls().ShouldBe(2);
        _feedback.Calls.ShouldNotContain(FakeDictationFeedback.Busy);
    }

    [Fact]
    public async Task CancelRequested_WhileIdleOrRecording_IsIgnored()
    {
        // Act
        _feedback.RequestCancel();
        await RecordAsync();
        _feedback.RequestCancel();
        _time.Advance(HoldDuration);
        Release();
        await WaitForAsync(FakeDictationFeedback.Idle);

        // Assert
        A.CallTo(() => _recorder.AbortAsync()).MustNotHaveHappened();
        A.CallTo(() => _transcriber.TranscribeAsync(Clip.Samples, PressOptions, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _inserter.InsertAsync(Transcript, Target, PressTextInsertion, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        _feedback.Calls.ShouldBe([
            FakeDictationFeedback.Starting, FakeDictationFeedback.Recording, FakeDictationFeedback.Transcribing,
            FakeDictationFeedback.Idle,
        ]);
        _feedback.Notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task CancelRequested_AfterTranscriptReady_InsertsText()
    {
        // Arrange
        var insertToken = CancellationToken.None;
        using var inserting = new ManualResetEventSlim();
        var insertion = new TaskCompletionSource<InsertionOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => _inserter.InsertAsync(A<string>._, A<InsertionTarget>._, A<TextInsertionSettings>._,
                A<CancellationToken>._))
            .ReturnsLazily((string _, InsertionTarget _, TextInsertionSettings _, CancellationToken token) =>
            {
                insertToken = token;
                inserting.Set();
                return insertion.Task;
            });
        await RecordAsync();
        _time.Advance(HoldDuration);
        Release();
        await WaitUntilAsync(() => inserting.IsSet);

        // Act: the press is handled after the cancel, so its "Still processing…" shows that the cancel was handled.
        _feedback.RequestCancel();
        Press();
        await WaitForAsync(FakeDictationFeedback.Busy);
        var insertCancelled = insertToken.IsCancellationRequested;
        insertion.SetResult(InsertionOutcome.Inserted);
        await WaitForAsync(FakeDictationFeedback.Idle);

        // Assert
        insertCancelled.ShouldBeFalse();
        A.CallTo(() => _inserter.InsertAsync(Transcript, Target, PressTextInsertion, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        _feedback.Notifications.ShouldBeEmpty();
        _logger.Entries.ShouldNotContain(entry => entry.Message.Contains("cancelled"));
    }

    [Fact]
    public async Task CancelRequested_DuringTranscription_LogsDurationsWithoutTranscript()
    {
        // Arrange
        var transcriptionToken = HoldTranscriptionUntilCancelled();
        await RecordAsync();
        _time.Advance(HoldDuration);
        Release();
        await transcriptionToken.WaitAsync(SignalTimeout, Ct);
        _time.Advance(TimeSpan.FromSeconds(2));

        // Act
        _feedback.RequestCancel();
        await WaitForAsync(FakeDictationFeedback.Idle);

        // Assert
        _logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Information &&
            entry.Message == "The dictation was cancelled after 2.00 s of processing, 1.00 s of audio");
        _logger.Entries.ShouldAllBe(entry => !entry.Message.Contains("Köln"));
    }

    [Fact]
    public async Task Pressed_EngineReady_IsActiveWithOneActivityWhileRecording()
    {
        // Act
        await RecordAsync();

        // Assert
        _dictationState.IsActive.ShouldBeTrue();
        _processActivity.Begun.ShouldBe([DictationController.ActivityReason]);
        _processActivity.Running.ShouldBe(1);
    }

    [Fact]
    public async Task Released_AfterOneSecond_EndsActivityAndIsInactiveOnceIdle()
    {
        // Arrange
        var changes = RecordActiveChanges();

        // Act
        await DictateAsync(HoldDuration);

        // Assert
        changes.ShouldBe([true, false]);
        _processActivity.Begun.ShouldBe([DictationController.ActivityReason]);
        _processActivity.Running.ShouldBe(0);
    }

    [Fact]
    public async Task Released_DuringTranscription_StaysActiveUntilInserted()
    {
        // Arrange
        var transcription = HoldTranscription();
        await RecordAsync();
        _time.Advance(HoldDuration);

        // Act
        Release();
        await WaitForAsync(FakeDictationFeedback.Transcribing);
        var activeWhileTranscribing = _dictationState.IsActive;
        transcription.SetResult(Result(Transcript));
        await WaitForAsync(FakeDictationFeedback.Idle);

        // Assert
        activeWhileTranscribing.ShouldBeTrue();
        _dictationState.IsActive.ShouldBeFalse();
        _processActivity.Running.ShouldBe(0);
    }

    [Theory]
    [InlineData("released within 200 ms")]
    [InlineData("cancelled")]
    [InlineData("recording failed")]
    public async Task Recording_EndsWithoutTranscription_EndsActivityAndIsInactive(string ending)
    {
        // Arrange
        await RecordAsync();

        // Act
        switch (ending)
        {
            case "released within 200 ms":
                _time.Advance(TimeSpan.FromMilliseconds(200));
                Release();
                break;
            case "cancelled":
                _hotkey.Cancelled += Raise.WithEmpty();
                break;
            default:
                _recorder.Failed += Raise.With<RecordingFailedException>(_recorder, new MicrophoneDisconnectedException());
                break;
        }

        await WaitForAsync(FakeDictationFeedback.Idle);

        // Assert
        _dictationState.IsActive.ShouldBeFalse();
        _processActivity.Running.ShouldBe(0);
    }

    [Fact]
    public async Task Pressed_StartThrowsRecordingFailed_EndsActivityAndIsInactive()
    {
        // Arrange
        A.CallTo(() => _recorder.StartAsync(A<TimeSpan>._, A<CancellationToken>._))
            .ThrowsAsync(new MicrophoneAccessDeniedException());
        var changes = RecordActiveChanges();

        // Act
        Press();
        await WaitForAsync(FakeDictationFeedback.Idle);

        // Assert
        changes.ShouldBe([true, false]);
        _processActivity.Begun.ShouldBe([DictationController.ActivityReason]);
        _processActivity.Running.ShouldBe(0);
    }

    [Fact]
    public async Task Pressed_EngineNotReady_NeverActiveWithoutActivity()
    {
        // Arrange
        A.CallTo(() => _transcriber.Status).Returns(TranscriberStatus.Loading);
        var changes = RecordActiveChanges();

        // Act
        Press();
        await WaitUntilAsync(() => LoadingNotifications() == 1);

        // Assert
        changes.ShouldBeEmpty();
        _processActivity.Begun.ShouldBeEmpty();
    }

    [Fact]
    public async Task StopAsync_WhileRecording_EndsActivityAndIsInactive()
    {
        // Arrange
        await RecordAsync();
        _time.Advance(HoldDuration);

        // Act
        await _sut.StopAsync(Ct).WaitAsync(SignalTimeout, Ct);

        // Assert
        _dictationState.IsActive.ShouldBeFalse();
        _processActivity.Running.ShouldBe(0);
    }

    [Fact]
    public async Task StopAsync_DuringTranscription_EndsActivityAndIsInactive()
    {
        // Arrange
        var transcription = HoldTranscriptionUntilCancelled();
        await RecordAsync();
        _time.Advance(HoldDuration);
        Release();
        await transcription.WaitAsync(SignalTimeout, Ct);

        // Act
        await _sut.StopAsync(Ct).WaitAsync(SignalTimeout, Ct);

        // Assert
        _dictationState.IsActive.ShouldBeFalse();
        _processActivity.Running.ShouldBe(0);
    }

    [Fact]
    public async Task StopAsync_WhileStartPending_EndsActivityAndIsInactive()
    {
        // Arrange
        A.CallTo(() => _recorder.StartAsync(A<TimeSpan>._, A<CancellationToken>._))
            .ReturnsLazily((TimeSpan _, CancellationToken token) => Task.Delay(Timeout.Infinite, token));
        Press();
        await WaitUntilAsync(() => StartCalls() == 1);

        // Act
        await _sut.StopAsync(Ct).WaitAsync(SignalTimeout, Ct);

        // Assert
        _dictationState.IsActive.ShouldBeFalse();
        _processActivity.Running.ShouldBe(0);
    }

    private static TranscriptionResult Result(string text)
    {
        return new TranscriptionResult(text, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(200));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            stopwatch.Elapsed.ShouldBeLessThan(SignalTimeout);
            await Task.Delay(5, Ct);
        }
    }

    private void Press()
    {
        _hotkey.Pressed += Raise.WithEmpty();
    }

    private void Release()
    {
        _hotkey.Released += Raise.WithEmpty();
    }

    private Task WaitForAsync(string call, int count = 1)
    {
        return WaitUntilAsync(() => _feedback.Count(call) >= count);
    }

    /// <summary>
    /// Presses the hotkey and waits until the recording runs.
    /// </summary>
    private async Task RecordAsync()
    {
        var recordings = _feedback.Count(FakeDictationFeedback.Recording);
        Press();
        await WaitForAsync(FakeDictationFeedback.Recording, recordings + 1);
    }

    /// <summary>
    /// Records, holds the hotkey for <paramref name="hold"/>, releases it and waits until the dictation has ended.
    /// </summary>
    private async Task DictateAsync(TimeSpan hold)
    {
        await RecordAsync();
        _time.Advance(hold);
        Release();
        await WaitForAsync(FakeDictationFeedback.Idle);
    }

    private TaskCompletionSource<TranscriptionResult> HoldTranscription()
    {
        var transcription = new TaskCompletionSource<TranscriptionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => _transcriber.TranscribeAsync(A<float[]>._, A<TranscriptionOptions>._, A<CancellationToken>._))
            .Returns(transcription.Task);
        return transcription;
    }

    /// <summary>
    /// Makes the transcription run until its token is cancelled, and ends it as cancelled then, as the engine does.
    /// </summary>
    /// <returns>A task that completes with the transcription's token once the transcription is called.</returns>
    private Task<CancellationToken> HoldTranscriptionUntilCancelled()
    {
        var called = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => _transcriber.TranscribeAsync(A<float[]>._, A<TranscriptionOptions>._, A<CancellationToken>._))
            .ReturnsLazily(async (float[] _, TranscriptionOptions _, CancellationToken token) =>
            {
                called.TrySetResult(token);
                await Task.Delay(Timeout.Infinite, token);
                return Result(Transcript);
            });
        return called.Task;
    }

    private void ReturnInsertionOutcome(InsertionOutcome outcome)
    {
        A.CallTo(() => _inserter.InsertAsync(A<string>._, A<InsertionTarget>._, A<TextInsertionSettings>._,
                A<CancellationToken>._))
            .Returns(outcome);
    }

    /// <summary>
    /// Records every value that <see cref="DictationState.ActiveChanged"/> reports, in order.
    /// </summary>
    private List<bool> RecordActiveChanges()
    {
        var changes = new List<bool>();
        _dictationState.ActiveChanged += (_, _) =>
        {
            lock (changes)
            {
                changes.Add(_dictationState.IsActive);
            }
        };
        return changes;
    }

    private int StartCalls()
    {
        return Fake.GetCalls(_recorder).Count(call => call.Method.Name == nameof(IAudioRecorder.StartAsync));
    }

    private int StatusReads()
    {
        return Fake.GetCalls(_transcriber).Count(call => call.Method.Name == $"get_{nameof(ITranscriber.Status)}");
    }

    private int LoadingNotifications()
    {
        return _feedback.Notifications.Count(notification => notification.Message == DictationMessages.LoadingMessage);
    }

    private void ShouldNotHaveTranscribed()
    {
        A.CallTo(() => _transcriber.TranscribeAsync(A<float[]>._, A<TranscriptionOptions>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    private void ShouldNotHaveInserted()
    {
        A.CallTo(() => _inserter.InsertAsync(A<string>._, A<InsertionTarget>._, A<TextInsertionSettings>._,
                A<CancellationToken>._))
            .MustNotHaveHappened();
    }
}
