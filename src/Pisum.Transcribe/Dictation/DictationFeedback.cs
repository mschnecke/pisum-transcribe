using Microsoft.Extensions.Hosting;
using Pisum.Transcribe.TextInsertion;
using Pisum.Transcribe.Transcription;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Dictation;

/// <summary>
/// Shows the dictation on the UI thread: the recording overlay, the tray icon with its tooltip, and notifications.
/// </summary>
/// <remarks>
/// This is the only writer of the tray icon and tooltip. One <see cref="Render"/> combines two inputs: the dictation
/// phase, set by the <c>Show*</c> calls, and the engine status. While a dictation runs, the tray shows its phase;
/// otherwise it shows the engine status, including a status that changed during the dictation. Stopping hides the
/// overlay in any phase and leaves the tray alone: at <b>Exit</b> the shutdown has already removed the tray icon,
/// and after an error the icon shows the error notification until the shutdown removes it.
/// <para>
/// The tray menu shows <b>Cancel transcription</b> in the transcribing phase, which lasts until the dictation ends, so
/// the item can also be chosen while the text is inserted.
/// </para>
/// </remarks>
internal sealed class DictationFeedback : IDictationFeedback, IHostedService
{
    /// <summary>
    /// How long a short overlay message, such as "No speech detected", stays visible.
    /// </summary>
    public static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(1.5);

    private readonly ITrayIconService _trayIcon;
    private readonly ITranscriber _transcriber;
    private readonly DictationIcons _icons;
    private readonly TimeProvider _timeProvider;
    private readonly Func<IRecordingOverlay> _createOverlay;
    private readonly Action<Action> _invokeOnUiThread;

    // Only touched on the UI thread. _messageTimer is set while a short message is shown; _messageId tells an elapsed
    // timer of a replaced or ended message to do nothing.
    private IRecordingOverlay? _overlay;
    private Phase _phase;
    private TranscriberStatus _status;
    private string? _backend;
    private ITimer? _messageTimer;
    private int _messageId;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="trayIcon">The tray icon.</param>
    /// <param name="transcriber">The transcription engine, whose status the tray shows between dictations.</param>
    /// <param name="icons">The state icons.</param>
    /// <param name="timeProvider">The time provider for short overlay messages.</param>
    /// <param name="createOverlay">
    /// Creates the overlay on the UI thread, for tests. <see langword="null"/> creates a
    /// <see cref="RecordingOverlayWindow"/>.
    /// </param>
    /// <param name="invokeOnUiThread">
    /// Queues an action on the UI thread, for tests. <see langword="null"/> uses the WPF dispatcher.
    /// </param>
    public DictationFeedback(ITrayIconService trayIcon,
                             ITranscriber transcriber,
                             DictationIcons icons,
                             TimeProvider timeProvider,
                             Func<IRecordingOverlay>? createOverlay = null,
                             Action<Action>? invokeOnUiThread = null)
    {
        _trayIcon = trayIcon;
        _transcriber = transcriber;
        _icons = icons;
        _timeProvider = timeProvider;
        _createOverlay = createOverlay ?? (() => new RecordingOverlayWindow());
        _invokeOnUiThread = invokeOnUiThread ?? (action => Application.Current.Dispatcher.InvokeAsync(action));
    }

    private enum Phase
    {
        Idle,
        Recording,
        Transcribing,
    }

    private IRecordingOverlay Overlay => _overlay ??= _createOverlay();

    /// <inheritdoc />
    public event EventHandler? CancelRequested;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _transcriber.StatusChanged += OnStatusChanged;
        _invokeOnUiThread(() =>
        {
            // Created now, so the first press shows the overlay without delay.
            _ = Overlay;

            _trayIcon.AddMenuItem(DictationMessages.CancelTranscriptionMenuItem,
                () => CancelRequested?.Invoke(this, EventArgs.Empty), () => _phase == Phase.Transcribing);

            // Read here, because TranscriberHostedService starts first and its load may already have changed the status.
            _status = _transcriber.Status;
            _backend = _transcriber.ActiveBackend;
            Render();
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _transcriber.StatusChanged -= OnStatusChanged;
        _invokeOnUiThread(() =>
        {
            // Runs after the Show* calls that the controller queued before it stopped.
            EndMessage();
            _overlay?.Hide();
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void ShowStarting(InsertionTarget target)
    {
        _invokeOnUiThread(() =>
        {
            EndMessage();
            Overlay.ShowStarting(target.WindowHandle);
        });
    }

    /// <inheritdoc />
    public void ShowRecording()
    {
        _invokeOnUiThread(() =>
        {
            EndMessage();
            SetPhase(Phase.Recording);
            Overlay.ShowRecording();
        });
    }

    /// <inheritdoc />
    public void ShowTranscribing()
    {
        _invokeOnUiThread(() =>
        {
            EndMessage();
            SetPhase(Phase.Transcribing);
            Overlay.ShowTranscribing();
        });
    }

    /// <inheritdoc />
    public void ShowBusy()
    {
        _invokeOnUiThread(() => ShowMessage(DictationMessages.OverlayBusy));
    }

    /// <inheritdoc />
    public void ShowNoSpeech()
    {
        _invokeOnUiThread(() => ShowMessage(DictationMessages.OverlayNoSpeech));
    }

    /// <inheritdoc />
    public void ShowIdle()
    {
        _invokeOnUiThread(() =>
        {
            SetPhase(Phase.Idle);

            // A shown message hides the overlay when it ends.
            if (_messageTimer is null)
            {
                Overlay.Hide();
            }
        });
    }

    /// <inheritdoc />
    public void Notify(string title, string message)
    {
        _invokeOnUiThread(() => _trayIcon.ShowNotification(title, message));
    }

    private void OnStatusChanged(object? sender, TranscriberStatus status)
    {
        // Read on the thread that changed the status, so the value belongs to this change.
        var backend = _transcriber.ActiveBackend;
        _invokeOnUiThread(() =>
        {
            _status = status;
            _backend = backend;
            Render();
        });
    }

    private void SetPhase(Phase phase)
    {
        _phase = phase;
        Render();
    }

    private void Render()
    {
        var (icon, state) = _phase switch
        {
            Phase.Recording => (_icons.Recording, DictationMessages.RecordingState),
            Phase.Transcribing => (_icons.Transcribing, DictationMessages.TranscribingState),
            _ => _status switch
            {
                TranscriberStatus.Ready => (_icons.Ready, DictationMessages.ReadyState(_backend)),
                TranscriberStatus.Loading => (_icons.Unavailable, DictationMessages.LoadingState),
                TranscriberStatus.Failed => (_icons.Unavailable, DictationMessages.FailedState),
                _ => (_icons.Unavailable, DictationMessages.NoModelState),
            },
        };
        _trayIcon.SetStatus(icon, DictationMessages.ToolTip(state));
    }

    private void ShowMessage(string text)
    {
        EndMessage();
        var messageId = _messageId;
        Overlay.ShowMessage(text);
        _messageTimer = _timeProvider.CreateTimer(_ => _invokeOnUiThread(() => OnMessageElapsed(messageId)), null,
            MessageDuration, Timeout.InfiniteTimeSpan);
    }

    private void OnMessageElapsed(int messageId)
    {
        if (messageId != _messageId)
        {
            return;
        }

        EndMessage();
        if (_phase == Phase.Transcribing)
        {
            Overlay.ShowTranscribing();
        }
        else if (_phase == Phase.Idle)
        {
            Overlay.Hide();
        }
    }

    private void EndMessage()
    {
        _messageTimer?.Dispose();
        _messageTimer = null;
        _messageId++;
    }
}
