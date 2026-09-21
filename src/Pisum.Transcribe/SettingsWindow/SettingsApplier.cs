using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Transcription;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// Applies saved settings while the application runs: a new hotkey takes effect at once, and a new model or backend
/// reloads the engine. Task, language and text insertion settings need nothing, because each dictation reads them at
/// its start.
/// </summary>
/// <remarks>
/// A model that is not installed yet is not loaded. The setup window selects its model before downloading it, and
/// <see cref="TranscriberHostedService"/> loads the selected model when the download finishes.
/// </remarks>
internal sealed class SettingsApplier : IHostedService
{
    private readonly ISettingsStore _settingsStore;
    private readonly IPushToTalkHotkey _hotkey;
    private readonly ITranscriber _transcriber;
    private readonly IModelStore _modelStore;
    private readonly ILogger<SettingsApplier> _logger;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="settingsStore">The settings store, whose saves are applied.</param>
    /// <param name="hotkey">The push-to-talk hotkey.</param>
    /// <param name="transcriber">The transcription engine.</param>
    /// <param name="modelStore">The model store, which tells whether the selected model is installed.</param>
    /// <param name="logger">The logger.</param>
    public SettingsApplier(ISettingsStore settingsStore,
                           IPushToTalkHotkey hotkey,
                           ITranscriber transcriber,
                           IModelStore modelStore,
                           ILogger<SettingsApplier> logger)
    {
        _settingsStore = settingsStore;
        _hotkey = hotkey;
        _transcriber = transcriber;
        _modelStore = modelStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _settingsStore.Changed += OnChanged;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _settingsStore.Changed -= OnChanged;
        return Task.CompletedTask;
    }

    private void OnChanged(object? sender, SettingsChangedEventArgs e)
    {
        var previous = e.Previous;
        var current = e.Current;

        if (previous.Recording != current.Recording)
        {
            _hotkey.SetHotkey(HotkeyParser.Parse(current.Recording.Hotkey, _logger));
            _logger.LogInformation("Applied the new push-to-talk hotkey");
        }

        var model = ModelCatalog.Resolve(current.Model.SelectedModelId);
        var backend = current.Transcription.Backend;
        if (model.Id == ModelCatalog.Resolve(previous.Model.SelectedModelId).Id &&
            backend == previous.Transcription.Backend)
        {
            return;
        }

        if (!_modelStore.IsInstalled(model))
        {
            _logger.LogInformation("Model {ModelId} is not installed, it loads once its download finishes", model.Id);
            return;
        }

        _logger.LogInformation("Reloading with model {ModelId} and backend {Backend}", model.Id, backend);
        _ = LoadAsync(model, backend);
    }

    private async Task LoadAsync(SpeechModel model, BackendPreference backend)
    {
        try
        {
            // Not awaited by the caller: the status changes report the progress.
            await _transcriber.LoadAsync(model, backend, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The application is stopping.
        }
    }
}
