using Microsoft.Extensions.Hosting;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Transcription;

/// <summary>
/// Loads the selected model in the background, at startup when it is installed or as soon as its download finishes. A
/// failure shows a notification. The tray icon and tooltip belong to the dictation feedback.
/// </summary>
internal sealed class TranscriberHostedService : IHostedService
{
    /// <summary>
    /// The notification title after a failure.
    /// </summary>
    public const string FailedTitle = "Model failed to load";

    private readonly ITranscriber _transcriber;
    private readonly IModelStore _modelStore;
    private readonly ISettingsStore _settingsStore;
    private readonly ITrayIconService _trayIcon;
    private readonly Action<Action> _invokeOnUiThread;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="transcriber">The transcription engine.</param>
    /// <param name="modelStore">The model store.</param>
    /// <param name="settingsStore">The settings store, already loaded.</param>
    /// <param name="trayIcon">The tray icon.</param>
    /// <param name="invokeOnUiThread">
    /// Queues an action on the UI thread, for tests. <see langword="null"/> uses the WPF dispatcher.
    /// </param>
    public TranscriberHostedService(ITranscriber transcriber,
                                    IModelStore modelStore,
                                    ISettingsStore settingsStore,
                                    ITrayIconService trayIcon,
                                    Action<Action>? invokeOnUiThread = null)
    {
        _transcriber = transcriber;
        _modelStore = modelStore;
        _settingsStore = settingsStore;
        _trayIcon = trayIcon;
        _invokeOnUiThread = invokeOnUiThread ?? (action => Application.Current.Dispatcher.InvokeAsync(action));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _transcriber.StatusChanged += OnStatusChanged;
        _modelStore.ModelInstalled += OnModelInstalled;

        var model = SelectedModel();
        if (_modelStore.IsInstalled(model))
        {
            _ = LoadAsync(model);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _modelStore.ModelInstalled -= OnModelInstalled;
        _transcriber.StatusChanged -= OnStatusChanged;
        return Task.CompletedTask;
    }

    private SpeechModel SelectedModel()
    {
        return ModelCatalog.Resolve(_settingsStore.Current.Model.SelectedModelId);
    }

    private void OnModelInstalled(object? sender, SpeechModel model)
    {
        if (model.Id == SelectedModel().Id)
        {
            _ = LoadAsync(model);
        }
    }

    private async Task LoadAsync(SpeechModel model)
    {
        try
        {
            // Not awaited by the callers: the status changes report the progress.
            await _transcriber.LoadAsync(model, _settingsStore.Current.Transcription.Backend, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The application is stopping.
        }
    }

    private void OnStatusChanged(object? sender, TranscriberStatus status)
    {
        if (status != TranscriberStatus.Failed)
        {
            return;
        }

        // Read on the thread that changed the status, so the value belongs to this change.
        var failureMessage = _transcriber.FailureMessage;
        _invokeOnUiThread(() =>
            _trayIcon.ShowNotification(FailedTitle, failureMessage ?? TranscribeCppTranscriber.LoadFailedMessage));
    }
}
