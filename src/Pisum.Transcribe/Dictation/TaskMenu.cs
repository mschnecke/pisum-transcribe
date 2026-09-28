using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Notifications;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.Transcription;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Dictation;

/// <summary>
/// Adds the task switch to the tray menu: two radio items, <b>Transcribe (&lt;source&gt;)</b> and
/// <b>Translate (&lt;source&gt; → &lt;target&gt;)</b>, which show the saved task and save the chosen one.
/// </summary>
/// <remarks>
/// The items read the settings each time the menu opens. Each dictation reads the task when it starts, so a saved task
/// applies from the next dictation on, and one in progress keeps its task.
/// </remarks>
internal sealed class TaskMenu : IHostedService
{
    private readonly ITrayIconService _trayIcon;
    private readonly ISettingsStore _settingsStore;
    private readonly INotifier _notifier;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<TaskMenu> _logger;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="trayIcon">The tray icon.</param>
    /// <param name="settingsStore">The settings store, already loaded.</param>
    /// <param name="notifier">Shows the notification of a failed save.</param>
    /// <param name="uiDispatcher">Reaches the UI thread.</param>
    /// <param name="lifetime">The application lifetime, whose stopping token cancels a save.</param>
    /// <param name="logger">The logger.</param>
    public TaskMenu(ITrayIconService trayIcon,
                    ISettingsStore settingsStore,
                    INotifier notifier,
                    IUiDispatcher uiDispatcher,
                    IHostApplicationLifetime lifetime,
                    ILogger<TaskMenu> logger)
    {
        _trayIcon = trayIcon;
        _settingsStore = settingsStore;
        _notifier = notifier;
        _uiDispatcher = uiDispatcher;
        _lifetime = lifetime;
        _logger = logger;
    }

    private TranscriptionSettings Settings => _settingsStore.Current.Transcription;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() =>
        {
            _trayIcon.AddMenuItem(() => DictationMessages.TranscribeMenuItem(Settings.SourceLanguage),
                () => _ = SwitchAsync(TranscriptionTask.Transcribe),
                isChecked: () => Settings.Task == TranscriptionTask.Transcribe);
            _trayIcon.AddMenuItem(
                () => DictationMessages.TranslateMenuItem(Settings.SourceLanguage, Settings.TargetLanguage),
                () => _ = SwitchAsync(TranscriptionTask.Translate),
                isChecked: () => Settings.Task == TranscriptionTask.Translate);
        });
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Saves <paramref name="task"/> as the task, unless it is the task already. A failed save is logged and shown as a
    /// notification.
    /// </summary>
    /// <param name="task">The chosen task.</param>
    /// <returns>A task that completes when the save ended, successfully or not.</returns>
    internal async Task SwitchAsync(TranscriptionTask task)
    {
        var current = _settingsStore.Current;
        if (current.Transcription.Task == task)
        {
            return;
        }

        var stopping = _lifetime.ApplicationStopping;
        try
        {
            await _settingsStore.SaveAsync(current with {Transcription = current.Transcription with {Task = task}},
                stopping);
            _logger.LogInformation("Switched the task to {Task} from the tray menu", task);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The application is ending.
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not save the task {Task} from the tray menu", task);
            _notifier.Show(DictationMessages.TaskSwitchFailedTitle, DictationMessages.TaskSwitchFailedMessage);
        }
    }
}
