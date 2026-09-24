using CommunityToolkit.Mvvm.ComponentModel;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Transcription;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// The model section of the settings window: the catalog models with download, delete and selection, the backend
/// preference, and the engine status. Use it on the UI thread.
/// </summary>
/// <remarks>
/// Downloads and deletes take effect at once; they are not part of the draft that <b>Save</b> applies. A download
/// belongs to the window: closing the window cancels it after asking.
/// </remarks>
internal sealed partial class ModelSectionViewModel : ObservableObject
{
    /// <summary>
    /// The engine status text while a model is loading.
    /// </summary>
    public const string LoadingText = "Loading model…";

    /// <summary>
    /// The engine status text while no model is loaded.
    /// </summary>
    public const string NotLoadedText = "No model is loaded.";

    /// <summary>
    /// The label of the GPU backend option, named for the platform's GPU backend, with its access key.
    /// </summary>
    public const string GpuBackendOptionText = "_" + TranscribeCppEngineFactory.GpuBackendName + " (GPU) only";

    private readonly IModelStore _modelStore;
    private readonly ITranscriber _transcriber;
    private readonly CancellationToken _applicationStopping;
    private readonly IUiDispatcher _uiDispatcher;
    private string _savedModelId;
    private bool _isEngineLoading;

    /// <summary>
    /// Initializes a new instance with the saved settings.
    /// </summary>
    /// <param name="settings">The saved settings.</param>
    /// <param name="modelStore">The model store.</param>
    /// <param name="transcriber">The transcription engine, whose status the section shows.</param>
    /// <param name="applicationStopping">Cancelled when the application stops; closing then asks nothing.</param>
    /// <param name="confirmDelete">Asks the user whether to delete a model.</param>
    /// <param name="uiDispatcher">Reaches the UI thread.</param>
    public ModelSectionViewModel(AppSettings settings,
                                 IModelStore modelStore,
                                 ITranscriber transcriber,
                                 CancellationToken applicationStopping,
                                 Func<SpeechModel, bool> confirmDelete,
                                 IUiDispatcher uiDispatcher)
    {
        _modelStore = modelStore;
        _transcriber = transcriber;
        _applicationStopping = applicationStopping;
        _uiDispatcher = uiDispatcher;
        _savedModelId = ModelCatalog.Resolve(settings.Model.SelectedModelId).Id;
        SelectedModelId = _savedModelId;
        Backend = settings.Transcription.Backend;
        EngineStatusText = string.Empty;
        Items = ModelCatalog.Models
            .Select(model => new ModelItemViewModel(ModelOption.Create(model), modelStore, Select, confirmDelete))
            .ToList();

        _transcriber.StatusChanged += OnEngineStatusChanged;
        _modelStore.ModelInstalled += OnModelInstalled;
        ShowEngineStatus(_transcriber.Status, _transcriber.ActiveBackend, _transcriber.FailureMessage);
    }

    /// <summary>
    /// The catalog models.
    /// </summary>
    public IReadOnlyList<ModelItemViewModel> Items { get; }

    /// <summary>
    /// The identifier of the model selected in the window.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedModel))]
    public partial string SelectedModelId { get; private set; }

    /// <summary>
    /// The model selected in the window.
    /// </summary>
    public SpeechModel SelectedModel => ModelCatalog.Resolve(SelectedModelId);

    /// <summary>
    /// The backend preference.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAutoBackend), nameof(IsGpuBackend), nameof(IsCpuBackend))]
    public partial BackendPreference Backend { get; set; }

    /// <summary>
    /// Whether the backend preference is <see cref="BackendPreference.Auto"/>, for a radio button.
    /// </summary>
    public bool IsAutoBackend
    {
        get => Backend == BackendPreference.Auto;
        set => SetBackend(value, BackendPreference.Auto);
    }

    /// <summary>
    /// Whether the backend preference is <see cref="BackendPreference.Gpu"/>, for a radio button.
    /// </summary>
    public bool IsGpuBackend
    {
        get => Backend == BackendPreference.Gpu;
        set => SetBackend(value, BackendPreference.Gpu);
    }

    /// <summary>
    /// Whether the backend preference is <see cref="BackendPreference.Cpu"/>, for a radio button.
    /// </summary>
    public bool IsCpuBackend
    {
        get => Backend == BackendPreference.Cpu;
        set => SetBackend(value, BackendPreference.Cpu);
    }

    /// <summary>
    /// The backend in use, such as <c>Ready on Metal</c>, or the engine status while it is not ready.
    /// </summary>
    [ObservableProperty]
    public partial string EngineStatusText { get; private set; }

    /// <summary>
    /// Whether a download started in this window is running.
    /// </summary>
    public bool IsDownloading => Items.Any(item => item.Download.IsDownloading);

    /// <summary>
    /// Selects a model in the window. A model that is not installed cannot be selected.
    /// </summary>
    /// <param name="modelId">The model identifier.</param>
    /// <returns><see langword="true"/> if the model is selected now.</returns>
    public bool SelectModel(string modelId)
    {
        var item = Items.SingleOrDefault(candidate => candidate.Model.Id == modelId);
        if (item is null || !item.IsInstalled)
        {
            UpdateItems();
            return false;
        }

        SelectedModelId = modelId;
        UpdateItems();
        return true;
    }

    /// <summary>
    /// Takes newly saved values for the settings the user has not edited, and marks the new active model.
    /// </summary>
    /// <param name="previous">The saved settings the section was based on.</param>
    /// <param name="current">The newly saved settings.</param>
    public void Rebase(AppSettings previous, AppSettings current)
    {
        var previousModelId = ModelCatalog.Resolve(previous.Model.SelectedModelId).Id;
        _savedModelId = ModelCatalog.Resolve(current.Model.SelectedModelId).Id;
        SelectedModelId = DraftValue.Rebase(SelectedModelId, previousModelId, _savedModelId);
        Backend = DraftValue.Rebase(Backend, previous.Transcription.Backend, current.Transcription.Backend);
        UpdateItems();
    }

    /// <summary>
    /// Reads the installed states again, for example when the window is activated.
    /// </summary>
    public void RefreshInstalled()
    {
        foreach (var item in Items)
        {
            item.RefreshInstalled();
        }
    }

    /// <summary>
    /// Decides whether the window may close. While a download started in the window runs, the user is asked first,
    /// and closing cancels the download. When the application stops, nothing is asked.
    /// </summary>
    /// <param name="confirmCancelDownload">Asks the user whether to cancel the download and close.</param>
    /// <returns><see langword="true"/> if the window may close.</returns>
    public bool ConfirmClose(Func<bool> confirmCancelDownload)
    {
        if (!IsDownloading || _applicationStopping.IsCancellationRequested)
        {
            return true;
        }

        if (!confirmCancelDownload())
        {
            return false;
        }

        foreach (var item in Items)
        {
            item.Download.Cancel();
        }

        return true;
    }

    /// <summary>
    /// Stops following the engine status and the model store, when the window has closed.
    /// </summary>
    public void Detach()
    {
        _transcriber.StatusChanged -= OnEngineStatusChanged;
        _modelStore.ModelInstalled -= OnModelInstalled;
    }

    private void SetBackend(bool isChosen, BackendPreference backend)
    {
        if (isChosen)
        {
            Backend = backend;
        }
    }

    private void Select(ModelItemViewModel item)
    {
        if (item.Model.Id != SelectedModelId)
        {
            SelectModel(item.Model.Id);
        }
    }

    private void UpdateItems()
    {
        foreach (var item in Items)
        {
            item.Update(item.Model.Id == SelectedModelId, item.Model.Id == _savedModelId, _isEngineLoading);
        }
    }

    private void OnEngineStatusChanged(object? sender, TranscriberStatus status)
    {
        // Read on the thread that changed the status, so the values belong to this change.
        var backend = _transcriber.ActiveBackend;
        var failureMessage = _transcriber.FailureMessage;
        _ = _uiDispatcher.InvokeAsync(() => ShowEngineStatus(status, backend, failureMessage));
    }

    private void ShowEngineStatus(TranscriberStatus status, string? backend, string? failureMessage)
    {
        EngineStatusText = status switch
        {
            TranscriberStatus.Ready => $"Ready on {backend}",
            TranscriberStatus.Loading => LoadingText,
            TranscriberStatus.Failed => failureMessage ?? TranscribeCppTranscriber.LoadFailedMessage,
            _ => NotLoadedText,
        };
        _isEngineLoading = status == TranscriberStatus.Loading;
        UpdateItems();
    }

    private void OnModelInstalled(object? sender, SpeechModel model)
    {
        _ = _uiDispatcher.InvokeAsync(() =>
            Items.SingleOrDefault(item => item.Model.Id == model.Id)?.RefreshInstalled());
    }
}
