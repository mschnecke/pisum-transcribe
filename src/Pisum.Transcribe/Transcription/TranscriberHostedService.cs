using Microsoft.Extensions.Hosting;
using Pisum.Transcribe.Notifications;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SpeechModels;

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
    private readonly INotifier _notifier;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="transcriber">The transcription engine.</param>
    /// <param name="modelStore">The model store.</param>
    /// <param name="settingsStore">The settings store, already loaded.</param>
    /// <param name="notifier">Shows the notification after a failure.</param>
    public TranscriberHostedService(ITranscriber transcriber,
                                    IModelStore modelStore,
                                    ISettingsStore settingsStore,
                                    INotifier notifier)
    {
        _transcriber = transcriber;
        _modelStore = modelStore;
        _settingsStore = settingsStore;
        _notifier = notifier;
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
        _notifier.Show(FailedTitle, _transcriber.FailureMessage ?? TranscribeCppTranscriber.LoadFailedMessage);
    }
}
