using Avalonia.Controls;
using Microsoft.Extensions.Hosting;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Permissions;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// Opens the setup window at startup when the setup is not complete, and adds the one setup item to the tray menu,
/// which is shown while the setup is not complete: <b>Download model…</b> while the selected model is not installed, or
/// on macOS in an app bundle <b>Set up Pisum Transcribe…</b>, also while Accessibility or the microphone is not granted.
/// </summary>
internal sealed class ModelSetupHostedService : IHostedService, ISetupWindow
{
    /// <summary>
    /// The menu item without permission rows.
    /// </summary>
    public const string DownloadModelHeader = "Download model…";

    /// <summary>
    /// The menu item with permission rows, on macOS in an app bundle.
    /// </summary>
    public const string SetUpHeader = "Set up Pisum Transcribe…";

    private readonly IModelStore _modelStore;
    private readonly ISettingsStore _settingsStore;
    private readonly ITrayIconService _trayIcon;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly PermissionsViewModel? _permissions;

    // Only touched on the UI thread.
    private ModelSetupWindow? _window;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="modelStore">The model store.</param>
    /// <param name="settingsStore">The settings store, already loaded.</param>
    /// <param name="trayIcon">The tray icon.</param>
    /// <param name="uiDispatcher">Reaches the UI thread.</param>
    /// <param name="lifetime">The application lifetime.</param>
    /// <param name="permissions">
    /// The permission rows on macOS in an app bundle, or <see langword="null"/>, where the model alone completes the
    /// setup.
    /// </param>
    public ModelSetupHostedService(IModelStore modelStore,
                                   ISettingsStore settingsStore,
                                   ITrayIconService trayIcon,
                                   IUiDispatcher uiDispatcher,
                                   IHostApplicationLifetime lifetime,
                                   PermissionsViewModel? permissions = null)
    {
        _modelStore = modelStore;
        _settingsStore = settingsStore;
        _trayIcon = trayIcon;
        _uiDispatcher = uiDispatcher;
        _lifetime = lifetime;
        _permissions = permissions;
    }

    /// <summary>
    /// The open setup window, or <see langword="null"/>, for tests. Read it on the UI thread.
    /// </summary>
    internal ModelSetupWindow? Window => _window;

    /// <inheritdoc />
    public bool IsOpen => _window is not null;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() =>
        {
            // Checked each time the menu opens, so the item also returns when a model file is deleted or a permission is
            // revoked while the app runs.
            _trayIcon.AddMenuItem(_permissions is null ? DownloadModelHeader : SetUpHeader, ShowWindow,
                () => !IsComplete());

            if (!IsComplete())
            {
                ShowWindow();
            }
        });
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private bool IsComplete()
    {
        var isModelInstalled =
            _modelStore.IsInstalled(ModelCatalog.Resolve(_settingsStore.Current.Model.SelectedModelId));
        return isModelInstalled && (_permissions is null || _permissions.ReadRequiredGranted());
    }

    private void ShowWindow()
    {
        if (_window is null)
        {
            _window = new ModelSetupWindow(new ModelSetupViewModel(_modelStore, _settingsStore, _lifetime, _permissions));
            _window.Closed += (_, _) =>
            {
                _permissions?.Close();
                _window = null;
            };
            _ = _permissions?.OpenAsync();

            // Show, not ShowDialog, so host startup continues.
            _window.Show();
        }
        else if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }
}
