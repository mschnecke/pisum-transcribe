using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.TextInsertion;
using Pisum.Transcribe.Transcription;
using Pisum.Transcribe.VoiceActivity;

namespace Pisum.Transcribe.Dictation;

/// <summary>
/// Drives push-to-talk dictation: the hotkey is pressed, the microphone records, the hotkey is released, the silence
/// around the speech is trimmed, the audio is transcribed, and the text is inserted into the window that was in the
/// foreground at the press.
/// </summary>
/// <remarks>
/// Hotkey and recorder events arrive on their own threads and are queued in a channel. One loop reads the channel and
/// owns all state, so events are handled one at a time in arrival order. The loop awaits only the recorder and the
/// cancel of a transcription, never the transcription itself.
/// Transcription and insertion run as a tracked task, which posts <see cref="DictationEventKind.ProcessingCompleted"/>
/// once the text is delivered; presses that arrive meanwhile are answered with "Still processing…" and dropped.
/// <b>Cancel transcription</b> in the tray menu only cancels the task's token, which the task observes until the
/// transcript is ready. The task then ends and posts <see cref="DictationEventKind.ProcessingCompleted"/> as well.
/// </remarks>
internal sealed class DictationController : IHostedService
{
    /// <summary>
    /// Recordings shorter than this, counted from when audio flows, are discarded.
    /// </summary>
    public static readonly TimeSpan MinimumRecordingDuration = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// The minimum time between two notifications with the same reason for not being ready.
    /// </summary>
    public static readonly TimeSpan NotReadyNotificationInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The reason of the process activity that each dictation runs in, as macOS lists it.
    /// </summary>
    public const string ActivityReason = "Dictation";

    private readonly IPushToTalkHotkey _hotkey;
    private readonly IAudioRecorder _recorder;
    private readonly ITranscriber _transcriber;
    private readonly IVoiceActivityDetector _voiceActivityDetector;
    private readonly IForegroundWindowTracker _tracker;
    private readonly ITextInserter _inserter;
    private readonly ISettingsStore _settingsStore;
    private readonly IDictationFeedback _feedback;
    private readonly DictationState _dictationState;
    private readonly IProcessActivity _processActivity;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DictationController> _logger;
    private readonly Channel<DictationEvent> _events =
        Channel.CreateUnbounded<DictationEvent>(new UnboundedChannelOptions {SingleReader = true});
    private readonly CancellationTokenSource _stopping = new();

    // Owned by the loop. StopAsync reads _state, _recordingStarted and _processing only after the loop has ended.
    // _processingCancellation is set while the state is Processing. _activity is set from BeginDictation to EndDictation.
    private readonly Dictionary<string, long> _notReadyNotified = [];
    private IDisposable? _activity;
    private State _state;
    private ActiveDictation? _dictation;
    private long _recordingStarted;
    private Task _processing = Task.CompletedTask;
    private CancellationTokenSource? _processingCancellation;

    private Task _loop = Task.CompletedTask;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="hotkey">The push-to-talk hotkey.</param>
    /// <param name="recorder">The microphone recorder.</param>
    /// <param name="transcriber">The transcription engine.</param>
    /// <param name="voiceActivityDetector">The speech detector, which trims the silence before transcription.</param>
    /// <param name="tracker">The foreground window tracker, which captures the insertion target.</param>
    /// <param name="inserter">The text inserter.</param>
    /// <param name="settingsStore">The settings store, read at each press.</param>
    /// <param name="feedback">The overlay, tray and notification feedback.</param>
    /// <param name="dictationState">Tells other services whether a dictation is in progress.</param>
    /// <param name="processActivity">Keeps macOS from throttling a dictation through App Nap.</param>
    /// <param name="timeProvider">The time provider for durations and the notification throttle.</param>
    /// <param name="logger">The logger.</param>
    public DictationController(IPushToTalkHotkey hotkey,
                               IAudioRecorder recorder,
                               ITranscriber transcriber,
                               IVoiceActivityDetector voiceActivityDetector,
                               IForegroundWindowTracker tracker,
                               ITextInserter inserter,
                               ISettingsStore settingsStore,
                               IDictationFeedback feedback,
                               DictationState dictationState,
                               IProcessActivity processActivity,
                               TimeProvider timeProvider,
                               ILogger<DictationController> logger)
    {
        _hotkey = hotkey;
        _recorder = recorder;
        _transcriber = transcriber;
        _voiceActivityDetector = voiceActivityDetector;
        _tracker = tracker;
        _inserter = inserter;
        _settingsStore = settingsStore;
        _feedback = feedback;
        _dictationState = dictationState;
        _processActivity = processActivity;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    private enum State
    {
        Idle,
        Recording,
        Processing,
    }

    private enum DictationEventKind
    {
        HotkeyPressed,
        HotkeyReleased,
        HotkeyCancelled,
        MaxDurationReached,
        RecordingFailed,
        ProcessingCompleted,
        CancelRequested,
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _hotkey.Pressed += OnHotkeyPressed;
        _hotkey.Released += OnHotkeyReleased;
        _hotkey.Cancelled += OnHotkeyCancelled;
        _recorder.MaxDurationReached += OnMaxDurationReached;
        _recorder.Failed += OnRecordingFailed;
        _feedback.CancelRequested += OnCancelRequested;
        _loop = Task.Run(RunAsync, CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Cancels a pending recorder start and a running speech detection, transcription or insertion, waits for the loop
    /// and the processing task to end, and then aborts a running recording, which releases the microphone.
    /// </summary>
    /// <param name="cancellationToken">Not used; the wait is bounded by the engine's cancellation.</param>
    /// <returns>A task that completes when the controller has stopped.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _hotkey.Pressed -= OnHotkeyPressed;
        _hotkey.Released -= OnHotkeyReleased;
        _hotkey.Cancelled -= OnHotkeyCancelled;
        _recorder.MaxDurationReached -= OnMaxDurationReached;
        _recorder.Failed -= OnRecordingFailed;
        _feedback.CancelRequested -= OnCancelRequested;

        await _stopping.CancelAsync().ConfigureAwait(false);
        _events.Writer.TryComplete();
        await _loop.ConfigureAwait(false);
        await _processing.ConfigureAwait(false);

        if (_state == State.Recording)
        {
            var duration = _timeProvider.GetElapsedTime(_recordingStarted);
            await _recorder.AbortAsync().ConfigureAwait(false);
            _state = State.Idle;

            // Written after the release, so a release that hangs at exit shows as a missing entry.
            _logger.LogInformation("The recording was aborted at exit after {RecordingSeconds:0.00} s",
                duration.TotalSeconds);
        }

        // A dictation that the stop interrupted, such as a pending start or a transcription, ends here, because the
        // loop no longer handles ProcessingCompleted. Without ShowIdle: the feedback hides the overlay as it stops.
        _state = State.Idle;
        EndActivity();
    }

    private static TranscriptionOptions ToOptions(TranscriptionSettings settings)
    {
        return new TranscriptionOptions(settings.Task, settings.SourceLanguage, settings.TargetLanguage);
    }

    private static double Seconds(float[] samples)
    {
        return (double) samples.Length / AudioClip.SampleRate;
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        Post(new DictationEvent(DictationEventKind.HotkeyPressed));
    }

    private void OnHotkeyReleased(object? sender, EventArgs e)
    {
        Post(new DictationEvent(DictationEventKind.HotkeyReleased));
    }

    private void OnHotkeyCancelled(object? sender, EventArgs e)
    {
        Post(new DictationEvent(DictationEventKind.HotkeyCancelled));
    }

    private void OnMaxDurationReached(object? sender, EventArgs e)
    {
        Post(new DictationEvent(DictationEventKind.MaxDurationReached));
    }

    private void OnRecordingFailed(object? sender, RecordingFailedException error)
    {
        Post(new DictationEvent(DictationEventKind.RecordingFailed, error));
    }

    private void OnCancelRequested(object? sender, EventArgs e)
    {
        Post(new DictationEvent(DictationEventKind.CancelRequested));
    }

    private void Post(DictationEvent dictationEvent)
    {
        // Fails only after StopAsync completed the channel.
        _events.Writer.TryWrite(dictationEvent);
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var dictationEvent in _events.Reader.ReadAllAsync(_stopping.Token).ConfigureAwait(false))
            {
                try
                {
                    await HandleAsync(dictationEvent).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    // Keeps the loop alive, so the next press can start a new dictation.
                    _logger.LogError(exception, "Handling {Event} failed in state {State}", dictationEvent.Kind, _state);
                    _feedback.Notify(DictationMessages.DictationFailedTitle, DictationMessages.DictationFailedMessage);
                    if (_state != State.Processing)
                    {
                        EndDictation();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // The application is stopping.
        }
    }

    private Task HandleAsync(DictationEvent dictationEvent)
    {
        switch (dictationEvent.Kind, _state)
        {
            case (DictationEventKind.HotkeyPressed, State.Idle):
                return StartRecordingAsync();
            case (DictationEventKind.HotkeyPressed, State.Processing):
                _feedback.ShowBusy();
                break;
            case (DictationEventKind.HotkeyReleased, State.Recording):
                return StopRecordingAsync(false);
            case (DictationEventKind.MaxDurationReached, State.Recording):
                return StopRecordingAsync(true);
            case (DictationEventKind.HotkeyCancelled, State.Recording):
                return AbortRecordingAsync();
            case (DictationEventKind.RecordingFailed, State.Recording):
                // The recorder has already released the microphone.
                FailRecording(dictationEvent.Error!);
                break;
            case (DictationEventKind.ProcessingCompleted, State.Processing):
                // Posted by the processing task as it ends, so nothing uses the source anymore.
                _processingCancellation!.Dispose();
                _processingCancellation = null;
                EndDictation();
                break;
            case (DictationEventKind.CancelRequested, State.Processing):
                // The processing task ends and posts ProcessingCompleted, which ends the dictation. Once the transcript
                // is ready, the task no longer observes the token.
                return _processingCancellation!.CancelAsync();

            // Any other event lost a race with one that was already handled, such as a limit reached just as the user
            // released the hotkey. Handling it would show a second notification or stop the recorder twice.
        }

        return Task.CompletedTask;
    }

    private async Task StartRecordingAsync()
    {
        var pressed = _timeProvider.GetTimestamp();
        var status = _transcriber.Status;
        if (status != TranscriberStatus.Ready)
        {
            NotifyNotReady(status);
            return;
        }

        BeginDictation();

        // Taken at the press, so a settings save during the dictation applies to the next one.
        var settings = _settingsStore.Current;
        var target = _tracker.CaptureForeground();
        _feedback.ShowStarting(target);

        try
        {
            // Hotkey events that arrive meanwhile wait in the channel.
            await _recorder.StartAsync(_transcriber.MaxInputDuration, _stopping.Token).ConfigureAwait(false);
        }
        catch (RecordingFailedException exception)
        {
            _feedback.Notify(DictationMessages.RecordingFailedTitle, exception.Message);
            EndDictation();
            return;
        }

        _recordingStarted = _timeProvider.GetTimestamp();
        _logger.LogInformation("Recording started {StartMilliseconds:0} ms after the press",
            _timeProvider.GetElapsedTime(pressed, _recordingStarted).TotalMilliseconds);
        _dictation = new ActiveDictation(target, ToOptions(settings.Transcription), settings.TextInsertion,
            settings.VoiceActivity.Enabled);
        _state = State.Recording;
        _feedback.ShowRecording();
    }

    private void NotifyNotReady(TranscriberStatus status)
    {
        var reason = status switch
        {
            TranscriberStatus.Loading => DictationMessages.LoadingMessage,
            TranscriberStatus.Failed => _transcriber.FailureMessage ?? DictationMessages.FailedMessage,
            _ => DictationMessages.NoModelMessage,
        };

        var now = _timeProvider.GetTimestamp();
        if (_notReadyNotified.TryGetValue(reason, out var notified) &&
            _timeProvider.GetElapsedTime(notified, now) < NotReadyNotificationInterval)
        {
            return;
        }

        _notReadyNotified[reason] = now;
        _feedback.Notify(DictationMessages.NotReadyTitle, reason);
    }

    private async Task StopRecordingAsync(bool maxDurationReached)
    {
        var dictation = _dictation!;

        // A release that waited in the channel during a slow start is handled right after it, so it is discarded here.
        if (!maxDurationReached && _timeProvider.GetElapsedTime(_recordingStarted) < MinimumRecordingDuration)
        {
            await _recorder.AbortAsync().ConfigureAwait(false);
            EndDictation();
            return;
        }

        AudioClip clip;
        try
        {
            clip = await _recorder.StopAsync().ConfigureAwait(false);
        }
        catch (RecordingFailedException exception)
        {
            // The microphone was lost just before the release or the limit.
            FailRecording(exception);
            return;
        }

        _dictation = null;
        _state = State.Processing;
        _processingCancellation = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        _feedback.ShowTranscribing();
        if (maxDurationReached)
        {
            _feedback.Notify(DictationMessages.MaxDurationTitle, DictationMessages.MaxDurationMessage);
        }

        var cancellationToken = _processingCancellation.Token;
        _processing = Task.Run(() => ProcessAsync(clip, dictation, cancellationToken), CancellationToken.None);
    }

    private async Task AbortRecordingAsync()
    {
        await _recorder.AbortAsync().ConfigureAwait(false);
        EndDictation();
    }

    private void FailRecording(RecordingFailedException exception)
    {
        _feedback.Notify(DictationMessages.RecordingFailedTitle, exception.Message);
        EndDictation();
    }

    /// <summary>
    /// Starts the span of a dictation: the process activity and the dictation state. <see cref="EndDictation"/> ends it.
    /// </summary>
    private void BeginDictation()
    {
        _activity = _processActivity.Begin(ActivityReason);
        _dictationState.SetActive(true);
    }

    private void EndDictation()
    {
        _state = State.Idle;
        _dictation = null;
        EndActivity();
        _feedback.ShowIdle();
    }

    private void EndActivity()
    {
        _activity?.Dispose();
        _activity = null;
        _dictationState.SetActive(false);
    }

    private async Task ProcessAsync(AudioClip clip, ActiveDictation dictation, CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        string? text = null;
        try
        {
            var samples = clip.Samples;
            if (dictation.VoiceActivityEnabled)
            {
                var trimmed = TrimSilence(samples, cancellationToken);
                if (trimmed is null)
                {
                    _feedback.ShowNoSpeech();
                    return;
                }

                samples = trimmed;
            }

            var result = await _transcriber.TranscribeAsync(samples, dictation.Options, cancellationToken)
                .ConfigureAwait(false);
            text = result.Text.Trim();
            if (text.Length == 0)
            {
                _feedback.ShowNoSpeech();
                return;
            }

            // Returns once the text is delivered; the clipboard restore finishes in the background. Observes only the
            // application stopping, so once the transcript is ready, a cancel has no effect.
            var outcome = await _inserter.InsertAsync(text, dictation.Target, dictation.TextInsertion, _stopping.Token)
                .ConfigureAwait(false);
            NotifyOutcome(outcome);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The user cancelled the dictation, or the application is stopping, which ends it silently.
            if (!_stopping.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "The dictation was cancelled after {ProcessingSeconds:0.00} s of processing, {AudioSeconds:0.00} s of audio",
                    _timeProvider.GetElapsedTime(started).TotalSeconds, Seconds(clip.Samples));
            }
        }
        catch (Exception exception) when (exception is LanguageNotSupportedException or AudioTooLongException
                                              or TranscriptionFailedException or TranscriberNotReadyException)
        {
            _logger.LogInformation("The dictation ended with {ExceptionType}", exception.GetType().Name);
            _feedback.Notify(DictationMessages.DictationFailedTitle, exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The dictation failed");
            _feedback.Notify(DictationMessages.DictationFailedTitle, DictationMessages.DictationFailedMessage);
        }
        finally
        {
            // Audio and transcript exist only for the duration of the dictation.
            clip = null!;
            text = null;
            Post(new DictationEvent(DictationEventKind.ProcessingCompleted));
        }
    }

    /// <summary>
    /// Cuts the silence before and after the speech.
    /// </summary>
    /// <param name="samples">The recording.</param>
    /// <param name="cancellationToken">Cancels the dictation.</param>
    /// <returns>
    /// The trimmed audio, <see langword="null"/> if there is no speech, or <paramref name="samples"/> if the detection
    /// failed.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// The dictation was cancelled, or the application is stopping.
    /// </exception>
    private float[]? TrimSilence(float[] samples, CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        try
        {
            var trimmed = AudioTrimmer.Trim(samples, _voiceActivityDetector.DetectSpeech(samples, cancellationToken));
            var elapsed = _timeProvider.GetElapsedTime(started).TotalMilliseconds;
            if (trimmed is null)
            {
                _logger.LogInformation(
                    "Voice activity detection found no speech in {OriginalSeconds:0.00} s of audio in {DetectionMilliseconds:0} ms",
                    Seconds(samples), elapsed);
            }
            else
            {
                _logger.LogInformation(
                    "Voice activity detection trimmed {OriginalSeconds:0.00} s of audio to {TrimmedSeconds:0.00} s in {DetectionMilliseconds:0} ms",
                    Seconds(samples), Seconds(trimmed), elapsed);
            }

            return trimmed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The dictation was cancelled, or the application is stopping, so there is nothing to fall back to.
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception,
                "Voice activity detection failed after {DetectionMilliseconds:0} ms, so {OriginalSeconds:0.00} s of audio are transcribed untrimmed",
                _timeProvider.GetElapsedTime(started).TotalMilliseconds, Seconds(samples));
            return samples;
        }
    }

    private void NotifyOutcome(InsertionOutcome outcome)
    {
        switch (outcome)
        {
            case InsertionOutcome.TargetWindowChanged:
                NotifyCopied(DictationMessages.TargetWindowChangedReason);
                break;
            case InsertionOutcome.TargetWindowElevated:
                NotifyCopied(DictationMessages.TargetWindowElevatedReason);
                break;
            case InsertionOutcome.SecureInputOn:
                NotifyCopied(DictationMessages.SecureInputReason);
                break;
            case InsertionOutcome.KeystrokesNotAllowed:
                NotifyCopied(DictationMessages.KeystrokesNotAllowedReason);
                break;
            case InsertionOutcome.ModifierKeysHeld:
                NotifyCopied(DictationMessages.ModifierKeysHeldReason);
                break;
            case InsertionOutcome.ClipboardUnavailable:
                _feedback.Notify(DictationMessages.NotDeliveredTitle, DictationMessages.ClipboardUnavailableMessage);
                break;
        }
    }

    private void NotifyCopied(string reason)
    {
        _feedback.Notify(DictationMessages.CopiedTitle, DictationMessages.CopiedMessage(reason));
    }

    /// <param name="Kind">What happened.</param>
    /// <param name="Error">The failure of <see cref="DictationEventKind.RecordingFailed"/>.</param>
    private readonly record struct DictationEvent(DictationEventKind Kind, RecordingFailedException? Error = null);

    /// <param name="Target">The window captured at the press.</param>
    /// <param name="Options">The transcription settings at the press.</param>
    /// <param name="TextInsertion">The text insertion settings at the press.</param>
    /// <param name="VoiceActivityEnabled">Whether the silence is trimmed, as set at the press.</param>
    private sealed record ActiveDictation(
        InsertionTarget Target,
        TranscriptionOptions Options,
        TextInsertionSettings TextInsertion,
        bool VoiceActivityEnabled);
}
