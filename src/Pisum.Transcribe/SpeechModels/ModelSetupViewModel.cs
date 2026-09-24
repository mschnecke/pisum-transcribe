using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Hosting;
using Pisum.Transcribe.Permissions;
using Pisum.Transcribe.Settings;

namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// The view model of the setup window: choose a catalog model, download it, and watch, cancel or retry the download.
/// On macOS it also holds the permission rows. Use it on the UI thread, and dispose it when the window has closed.
/// </summary>
internal sealed partial class ModelSetupViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsStore _settingsStore;
    private readonly CancellationToken _applicationStopping;
    private readonly ModelDownloadViewModel _download;
    private bool _isConfirmingClose;

    /// <summary>
    /// Initializes a new instance with the selected model from the settings preselected.
    /// </summary>
    /// <param name="modelStore">The model store.</param>
    /// <param name="settingsStore">The settings store.</param>
    /// <param name="lifetime">The application lifetime.</param>
    /// <param name="permissions">
    /// The permission rows on macOS in an app bundle, or <see langword="null"/>, where the model alone completes the
    /// setup.
    /// </param>
    public ModelSetupViewModel(IModelStore modelStore,
                               ISettingsStore settingsStore,
                               IHostApplicationLifetime lifetime,
                               PermissionsViewModel? permissions = null)
    {
        _settingsStore = settingsStore;
        _applicationStopping = lifetime.ApplicationStopping;
        _download = new ModelDownloadViewModel(modelStore);
        _download.PropertyChanged += OnDownloadPropertyChanged;
        Permissions = permissions;
        if (permissions is not null)
        {
            permissions.PropertyChanged += OnPermissionsPropertyChanged;
        }

        Models = ModelCatalog.Models.Select(ModelOption.Create).ToList();
        var selectedModel = ModelCatalog.Resolve(settingsStore.Current.Model.SelectedModelId);
        SelectedModel = Models.Single(option => option.Model == selectedModel);
        IsModelInstalled = modelStore.IsInstalled(selectedModel);
    }

    /// <summary>
    /// Raised when the window should close: when the setup is complete (<see cref="IsComplete"/>), after a download or a
    /// permission change in either order, or when the user chooses <b>Close</b>.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// The permission rows on macOS in an app bundle, or <see langword="null"/>.
    /// </summary>
    public PermissionsViewModel? Permissions { get; }

    /// <summary>
    /// Whether the window shows the permission rows.
    /// </summary>
    public bool HasPermissions => Permissions is not null;

    /// <summary>
    /// The window's heading.
    /// </summary>
    public string Heading => HasPermissions ? "Set up Pisum Transcribe" : "Download a speech model";

    /// <summary>
    /// The text below the heading.
    /// </summary>
    public string IntroText => HasPermissions
        ? "Pisum Transcribe needs a speech model, Accessibility and the microphone before you can dictate. The model " +
          "runs on this Mac."
        : "Pisum Transcribe needs a speech model before you can dictate. Choose a model and download it. The model " +
          "runs on this computer.";

    /// <summary>
    /// Whether the selected model is installed. The model part then collapses to <see cref="InstalledModelText"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstalledModelText))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial bool IsModelInstalled { get; private set; }

    /// <summary>
    /// The model part as one line, such as <c>Speech model: Canary 1B Flash, installed</c>.
    /// </summary>
    public string InstalledModelText => $"Speech model: {SelectedModel.Model.DisplayName}, installed";

    /// <summary>
    /// Whether the setup is complete: the selected model is installed and, with permission rows, both required
    /// permissions are granted and in effect. The optional permissions don't count.
    /// </summary>
    public bool IsComplete => IsModelInstalled && (Permissions is null || Permissions.AreRequiredGranted);

    /// <summary>
    /// The catalog models.
    /// </summary>
    public IReadOnlyList<ModelOption> Models { get; }

    /// <summary>
    /// The highlighted model: the one to download, whose attribution the window shows.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstalledModelText))]
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

            // The setup may have completed while the user was asked. Its close request was held back, because a
            // window cannot be closed while it asks whether to close.
            return IsComplete;
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
        return !IsDownloading && !IsModelInstalled;
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
        if (installed)
        {
            IsModelInstalled = true;
            RequestCloseWhenComplete();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Permissions is not null)
        {
            Permissions.PropertyChanged -= OnPermissionsPropertyChanged;
        }
    }

    private void RequestCloseWhenComplete()
    {
        if (IsComplete && !_isConfirmingClose)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPermissionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PermissionsViewModel.AreRequiredGranted))
        {
            RequestCloseWhenComplete();
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
