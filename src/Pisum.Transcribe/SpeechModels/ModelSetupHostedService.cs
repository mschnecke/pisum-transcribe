using Microsoft.Extensions.Hosting;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// Opens the setup window at startup when the selected model is not installed, and adds the tray item
/// <b>Download model…</b>, which is shown while the selected model is not installed.
/// </summary>
internal sealed class ModelSetupHostedService : IHostedService
{
    private readonly IModelStore _modelStore;
    private readonly ISettingsStore _settingsStore;
    private readonly ITrayIconService _trayIcon;
    private readonly IHostApplicationLifetime _lifetime;

    // Only touched on the UI thread.
    private ModelSetupWindow? _window;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="modelStore">The model store.</param>
    /// <param name="settingsStore">The settings store, already loaded.</param>
    /// <param name="trayIcon">The tray icon.</param>
    /// <param name="lifetime">The application lifetime.</param>
    public ModelSetupHostedService(IModelStore modelStore,
                                   ISettingsStore settingsStore,
                                   ITrayIconService trayIcon,
                                   IHostApplicationLifetime lifetime)
    {
        _modelStore = modelStore;
        _settingsStore = settingsStore;
        _trayIcon = trayIcon;
        _lifetime = lifetime;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // Checked each time the menu opens, so the item also returns when a model file is deleted while the app runs.
            _trayIcon.AddMenuItem("Download model…", ShowWindow, () => !IsSelectedModelInstalled());

            if (!IsSelectedModelInstalled())
            {
                ShowWindow();
            }
        }).Task;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private bool IsSelectedModelInstalled()
    {
        return _modelStore.IsInstalled(ModelCatalog.Resolve(_settingsStore.Current.Model.SelectedModelId));
    }

    private void ShowWindow()
    {
        if (_window is null)
        {
            _window = new ModelSetupWindow(new ModelSetupViewModel(_modelStore, _settingsStore, _lifetime));
            _window.Closed += (_, _) => _window = null;

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
