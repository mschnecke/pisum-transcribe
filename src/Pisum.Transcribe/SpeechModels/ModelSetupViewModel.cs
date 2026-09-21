using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Hosting;
using Pisum.Transcribe.Settings;

namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// The view model of the setup window: choose a catalog model, download it, and watch, cancel or retry the download.
/// Use it on the UI thread.
/// </summary>
internal sealed partial class ModelSetupViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly CancellationToken _applicationStopping;
    private readonly ModelDownloadViewModel _download;
    private bool _isConfirmingClose;
    private bool _isInstalled;

    /// <summary>
    /// Initializes a new instance with the selected model from the settings preselected.
    /// </summary>
    /// <param name="modelStore">The model store.</param>
    /// <param name="settingsStore">The settings store.</param>
    /// <param name="lifetime">The application lifetime.</param>
    public ModelSetupViewModel(IModelStore modelStore, ISettingsStore settingsStore, IHostApplicationLifetime lifetime)
    {
        _settingsStore = settingsStore;
        _applicationStopping = lifetime.ApplicationStopping;
        _download = new ModelDownloadViewModel(modelStore);
        _download.PropertyChanged += OnDownloadPropertyChanged;

        Models = ModelCatalog.Models.Select(ModelOption.Create).ToList();
        var selectedModel = ModelCatalog.Resolve(settingsStore.Current.Model.SelectedModelId);
        SelectedModel = Models.Single(option => option.Model == selectedModel);
    }

    /// <summary>
    /// Raised when the window should close, after a successful download or when the user chooses <b>Close</b>.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// The catalog models.
    /// </summary>
    public IReadOnlyList<ModelOption> Models { get; }

    /// <summary>
    /// The highlighted model: the one to download, whose attribution the window shows.
    /// </summary>
    [ObservableProperty]
    public partial ModelOption SelectedModel { get; set; }

    /// <summary>
    /// Whether a download is running.
    /// </summary>
    public bool IsDownloading => _download.IsDownloading;

    /// <summary>
    /// The download progress from 0 to 100.
    /// </summary>
    public double ProgressPercent => _download.ProgressPercent;

    /// <summary>
    /// The download progress as text, such as <c>0.50 of 1.07 GB</c>.
    /// </summary>
    public string ProgressText => _download.ProgressText;

    /// <summary>
    /// Why the last download failed, or <see langword="null"/>.
    /// </summary>
    public string? ErrorMessage => _download.ErrorMessage;

    /// <summary>
    /// Whether the last download failed.
    /// </summary>
    public bool HasError => _download.HasError;

    /// <summary>
    /// Decides whether the window may close. While a download runs, the user is asked first, and closing cancels the
    /// download. When the application stops, nothing is asked, because the model store has already cancelled the
    /// download.
    /// </summary>
    /// <param name="confirmCancelDownload">Asks the user whether to cancel the download and close.</param>
    /// <returns><see langword="true"/> if the window may close.</returns>
    public bool ConfirmClose(Func<bool> confirmCancelDownload)
    {
        if (!IsDownloading || _applicationStopping.IsCancellationRequested)
        {
            return true;
        }

        _isConfirmingClose = true;
        try
        {
            if (confirmCancelDownload())
            {
                _download.Cancel();
                return true;
            }

            // The download may have succeeded while the user was asked. Its close request was held back, because a
            // window cannot be closed while it asks whether to close.
            return _isInstalled;
        }
        finally
        {
            _isConfirmingClose = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task DownloadAsync()
    {
        return InstallSelectedModelAsync();
    }

    private bool CanDownload()
    {
        return !IsDownloading;
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private Task RetryAsync()
    {
        return InstallSelectedModelAsync();
    }

    private bool CanRetry()
    {
        return HasError && !IsDownloading;
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _download.Cancel();
    }

    private bool CanCancel()
    {
        return IsDownloading;
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task InstallSelectedModelAsync()
    {
        var model = SelectedModel.Model;

        // Saved before the download, so the model is already selected when IModelStore.ModelInstalled is raised.
        var installed = await _download.DownloadAsync(model, () =>
        {
            var settings = _settingsStore.Current;
            return _settingsStore.SaveAsync(settings with {Model = settings.Model with {SelectedModelId = model.Id}},
                CancellationToken.None);
        });
        if (!installed)
        {
            return;
        }

        _isInstalled = true;
        if (!_isConfirmingClose)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnDownloadPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The forwarded properties have the same names.
        OnPropertyChanged(e.PropertyName);
        if (e.PropertyName is nameof(IsDownloading) or nameof(ErrorMessage))
        {
            DownloadCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
            RetryCommand.NotifyCanExecuteChanged();
        }
    }
}
