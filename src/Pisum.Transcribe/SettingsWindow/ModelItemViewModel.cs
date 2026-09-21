using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pisum.Transcribe.SpeechModels;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// One catalog model in the model section: its description and installed state, and its download, delete and
/// selection. Use it on the UI thread.
/// </summary>
internal sealed partial class ModelItemViewModel : ObservableObject
{
    /// <summary>
    /// The message when a model file could not be deleted.
    /// </summary>
    public const string DeleteFailedMessage =
        "The model could not be deleted. It may still be in use. Try again in a moment.";

    private readonly IModelStore _modelStore;
    private readonly Action<ModelItemViewModel> _select;
    private readonly Func<SpeechModel, bool> _confirmDelete;
    private bool _isEngineLoading;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="option">The catalog model with its formatted size and languages.</param>
    /// <param name="modelStore">The model store.</param>
    /// <param name="select">Selects this model in the window.</param>
    /// <param name="confirmDelete">Asks the user whether to delete a model.</param>
    public ModelItemViewModel(ModelOption option,
                              IModelStore modelStore,
                              Action<ModelItemViewModel> select,
                              Func<SpeechModel, bool> confirmDelete)
    {
        Option = option;
        _modelStore = modelStore;
        _select = select;
        _confirmDelete = confirmDelete;
        Download = new ModelDownloadViewModel(modelStore);
        Download.PropertyChanged += OnDownloadPropertyChanged;
        IsInstalled = modelStore.IsInstalled(option.Model);
    }

    /// <summary>
    /// The catalog model with its formatted size and languages.
    /// </summary>
    public ModelOption Option { get; }

    /// <summary>
    /// The catalog model.
    /// </summary>
    public SpeechModel Model => Option.Model;

    /// <summary>
    /// The download of this model.
    /// </summary>
    public ModelDownloadViewModel Download { get; }

    /// <summary>
    /// Whether the model file is installed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsDelete))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand), nameof(DeleteCommand))]
    public partial bool IsInstalled { get; private set; }

    /// <summary>
    /// Whether the model is selected in the window. Setting it selects the model, if it is installed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsDelete))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial bool IsSelected { get; set; }

    /// <summary>
    /// Whether the model is the saved active model.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsDelete))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial bool IsActive { get; private set; }

    /// <summary>
    /// Whether <b>Delete</b> is offered: the model is installed, and neither the saved active model nor the model
    /// selected in the window.
    /// </summary>
    public bool ShowsDelete => IsInstalled && !IsSelected && !IsActive;

    /// <summary>
    /// Why the last delete failed, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial string? DeleteError { get; private set; }

    /// <summary>
    /// Updates the state that the model section owns.
    /// </summary>
    /// <param name="isSelected">Whether the model is selected in the window.</param>
    /// <param name="isActive">Whether the model is the saved active model.</param>
    /// <param name="isEngineLoading">Whether the engine is loading a model, which blocks deleting.</param>
    public void Update(bool isSelected, bool isActive, bool isEngineLoading)
    {
        IsSelected = isSelected;
        IsActive = isActive;
        if (_isEngineLoading != isEngineLoading)
        {
            _isEngineLoading = isEngineLoading;
            DeleteCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Reads the installed state again, for example after another window installed the model or the engine deleted a
    /// damaged file.
    /// </summary>
    public void RefreshInstalled()
    {
        IsInstalled = _modelStore.IsInstalled(Model);
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            _select(this);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        await Download.DownloadAsync(Model);
        RefreshInstalled();
    }

    private bool CanDownload()
    {
        return !IsInstalled && !Download.IsDownloading;
    }

    [RelayCommand(CanExecute = nameof(CanCancelDownload))]
    private void CancelDownload()
    {
        Download.Cancel();
    }

    private bool CanCancelDownload()
    {
        return Download.IsDownloading;
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (!_confirmDelete(Model))
        {
            return;
        }

        DeleteError = null;
        try
        {
            _modelStore.Delete(Model);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or InvalidOperationException)
        {
            // The model store logs the details.
            DeleteError = DeleteFailedMessage;
        }

        RefreshInstalled();
    }

    private bool CanDelete()
    {
        return ShowsDelete && !_isEngineLoading;
    }

    private void OnDownloadPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModelDownloadViewModel.IsDownloading))
        {
            DownloadCommand.NotifyCanExecuteChanged();
            CancelDownloadCommand.NotifyCanExecuteChanged();
        }
    }
}
