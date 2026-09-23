using Avalonia.Controls;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Transcription;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// Adds the tray item <b>Settings…</b> and opens the settings window from it or from a click on the tray icon.
/// There is only one settings window; opening it again brings the open one to the front.
/// </summary>
internal sealed class SettingsWindowService : IHostedService
{
    /// <summary>
    /// The tray menu item that opens the settings window.
    /// </summary>
    public const string MenuItemHeader = "Settings…";

    private readonly ITrayIconService _trayIcon;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ISettingsStore _settingsStore;
    private readonly IStartupRegistration _startupRegistration;
    private readonly IModelStore _modelStore;
    private readonly ITranscriber _transcriber;
    private readonly IPushToTalkHotkey _hotkey;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<SettingsViewModel> _viewModelLogger;

    // Only touched on the UI thread.
    private SettingsDialog? _dialog;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="trayIcon">The tray icon.</param>
    /// <param name="uiDispatcher">Reaches the UI thread.</param>
    /// <param name="settingsStore">The settings store.</param>
    /// <param name="startupRegistration">Starts the application at sign-in.</param>
    /// <param name="modelStore">The model store.</param>
    /// <param name="transcriber">The transcription engine.</param>
    /// <param name="hotkey">The push-to-talk hotkey.</param>
    /// <param name="lifetime">The application lifetime.</param>
    /// <param name="viewModelLogger">The logger of the settings view model.</param>
    public SettingsWindowService(ITrayIconService trayIcon,
                                 IUiDispatcher uiDispatcher,
                                 ISettingsStore settingsStore,
                                 IStartupRegistration startupRegistration,
                                 IModelStore modelStore,
                                 ITranscriber transcriber,
                                 IPushToTalkHotkey hotkey,
                                 IHostApplicationLifetime lifetime,
                                 ILogger<SettingsViewModel> viewModelLogger)
    {
        _trayIcon = trayIcon;
        _uiDispatcher = uiDispatcher;
        _settingsStore = settingsStore;
        _startupRegistration = startupRegistration;
        _modelStore = modelStore;
        _transcriber = transcriber;
        _hotkey = hotkey;
        _lifetime = lifetime;
        _viewModelLogger = viewModelLogger;
    }

    /// <summary>
    /// The open settings window, or <see langword="null"/>, for tests.
    /// </summary>
    internal SettingsDialog? Dialog => _dialog;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() =>
        {
            _trayIcon.AddMenuItem(MenuItemHeader, ShowDialog);
            _trayIcon.Clicked += OnClicked;
        });
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _trayIcon.Clicked -= OnClicked;
        return Task.CompletedTask;
    }

    private void OnClicked(object? sender, EventArgs e)
    {
        ShowDialog();
    }

    private void ShowDialog()
    {
        if (_dialog is null)
        {
            SettingsDialog? dialog = null;
            var viewModel = new SettingsViewModel(_settingsStore, _startupRegistration, _modelStore, _transcriber,
                _hotkey, _lifetime, model => dialog!.ConfirmDelete(model), _viewModelLogger, _uiDispatcher);
            dialog = new SettingsDialog(viewModel);
            dialog.Closed += (_, _) => _dialog = null;
            _dialog = dialog;

            // Show, not ShowDialog: the application has no main window, and the tray must stay usable.
            dialog.Show();
        }
        else if (_dialog.WindowState == WindowState.Minimized)
        {
            _dialog.WindowState = WindowState.Normal;
        }

        _dialog.Activate();
    }
}
