using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Dictation;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.SpeechModels;

namespace Pisum.Transcribe.Permissions;

/// <summary>
/// Restarts the application when Accessibility is granted while it runs, because the keyboard hook sees the grant only
/// in a new process (design D4 of add-macos-setup). It checks the grant every <see cref="CheckInterval"/> while it's
/// not in effect: from the start when the process started without it, and from a revoke on otherwise (design D4 of
/// add-macos-recording). Only a change from not granted to granted counts, so a process that keeps its grant never
/// restarts. The restart waits until no model download runs, and shows a notice in the setup window for
/// <see cref="NoticeDuration"/> when it's open. A download that starts during the notice delays the restart again. A
/// dictation in progress at the moment of the restart delays it until the dictation has ended (design D7 of
/// add-macos-dictation).
/// </summary>
internal sealed class RelaunchService : IHostedService
{
    /// <summary>
    /// How often Accessibility is checked while it's not granted.
    /// </summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long the setup window says that the application restarts, before it does.
    /// </summary>
    public static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The Accessibility row's note while a download delays the restart.
    /// </summary>
    public const string WaitingForDownloadText = "Restarts when the download is finished";

    /// <summary>
    /// The Accessibility row's note before the restart.
    /// </summary>
    public const string RestartingText = "Pisum Transcribe restarts to turn on the hotkey";

    private readonly IPermissions _permissions;
    private readonly PermissionsViewModel? _viewModel;
    private readonly IModelStore _modelStore;
    private readonly IDictationState _dictationState;
    private readonly ISetupWindow _setupWindow;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RelaunchService> _logger;
    private readonly string? _bundlePath;

    // Only touched on the UI thread.
    private ITimer? _checkTimer;
    private ITimer? _noticeTimer;
    private bool _granted;
    private bool _restarting;
    private bool _waitingForDictation;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="permissions">Reads the Accessibility grant.</param>
    /// <param name="viewModel">The setup window's permission rows, or <see langword="null"/> outside an app bundle.</param>
    /// <param name="modelStore">Tells whether a download runs.</param>
    /// <param name="dictationState">Tells whether a dictation is in progress.</param>
    /// <param name="setupWindow">Tells whether the setup window is open.</param>
    /// <param name="uiDispatcher">Reaches the UI thread.</param>
    /// <param name="timeProvider">The time provider for the checks and the notice.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="bundlePath">The app bundle the application runs from, or <see langword="null"/> without one.</param>
    public RelaunchService(IPermissions permissions,
                           PermissionsViewModel? viewModel,
                           IModelStore modelStore,
                           IDictationState dictationState,
                           ISetupWindow setupWindow,
                           IUiDispatcher uiDispatcher,
                           TimeProvider timeProvider,
                           ILogger<RelaunchService> logger,
                           string? bundlePath)
    {
        _permissions = permissions;
        _viewModel = viewModel;
        _modelStore = modelStore;
        _dictationState = dictationState;
        _setupWindow = setupWindow;
        _uiDispatcher = uiDispatcher;
        _timeProvider = timeProvider;
        _logger = logger;
        _bundlePath = bundlePath;
    }

    /// <summary>
    /// Raised on the UI thread when the application should restart. <c>App</c> ends it with
    /// <see cref="ShutdownReason.Relaunch"/>.
    /// </summary>
    public event EventHandler? RelaunchRequested;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Without a view model the permissions are skipped outside an app bundle, which is logged there.
        if (_viewModel is null)
        {
            return Task.CompletedTask;
        }

        if (_bundlePath is null)
        {
            if (!_permissions.IsAccessibilityInEffect)
            {
                _logger.LogInformation(
                    "No restart after the Accessibility grant, because no app bundle encloses the application");
            }

            return Task.CompletedTask;
        }

        return _uiDispatcher.InvokeAsync(() =>
        {
            _permissions.AccessibilityInEffectChanged += OnAccessibilityInEffectChanged;
            StartCheckingWhenNotInEffect();
        });
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() =>
        {
            _permissions.AccessibilityInEffectChanged -= OnAccessibilityInEffectChanged;
            _checkTimer?.Dispose();
            _noticeTimer?.Dispose();
            _modelStore.DownloadStateChanged -= OnDownloadStateChanged;
            _dictationState.ActiveChanged -= OnDictationActiveChanged;
        });
    }

    private void OnAccessibilityInEffectChanged(object? sender, EventArgs e)
    {
        // Raised on the UI thread.
        StartCheckingWhenNotInEffect();
    }

    private void StartCheckingWhenNotInEffect()
    {
        if (_permissions.IsAccessibilityInEffect || _checkTimer is not null || _granted)
        {
            return;
        }

        _checkTimer = _timeProvider.CreateTimer(_ => _ = _uiDispatcher.InvokeAsync(CheckAccessibility), null,
            CheckInterval, CheckInterval);
    }

    private void CheckAccessibility()
    {
        if (_granted || _permissions.GetState(Permission.Accessibility) != PermissionState.Granted)
        {
            return;
        }

        _granted = true;
        _checkTimer?.Dispose();
        _logger.LogInformation("Accessibility was granted, restarting to turn on the hotkey");

        // Subscribed before the check, so a download that ends in between isn't missed.
        _modelStore.DownloadStateChanged += OnDownloadStateChanged;
        RestartWhenNoDownload();
    }

    private void OnDownloadStateChanged(object? sender, EventArgs e)
    {
        _ = _uiDispatcher.InvokeAsync(RestartWhenNoDownload);
    }

    private void RestartWhenNoDownload()
    {
        if (_restarting)
        {
            return;
        }

        if (_modelStore.IsDownloading)
        {
            // A download that starts during the notice ends the notice, so the restart doesn't cancel it.
            _noticeTimer?.Dispose();
            _noticeTimer = null;
            if (_viewModel!.Accessibility.Note != WaitingForDownloadText)
            {
                _logger.LogInformation("The restart waits until the model download has ended");
            }

            _viewModel.Accessibility.Note = WaitingForDownloadText;
            return;
        }

        if (_noticeTimer is not null)
        {
            return;
        }

        if (!_setupWindow.IsOpen)
        {
            Relaunch();
            return;
        }

        _viewModel!.Accessibility.Note = RestartingText;
        ITimer? noticeTimer = null;
        noticeTimer = _timeProvider.CreateTimer(_ => _ = _uiDispatcher.InvokeAsync(() =>
        {
            // The callback of a notice that a download ended may still run after it was queued.
            if (_noticeTimer == noticeTimer)
            {
                Relaunch();
            }
        }), null, NoticeDuration, Timeout.InfiniteTimeSpan);
        _noticeTimer = noticeTimer;
    }

    private void OnDictationActiveChanged(object? sender, EventArgs e)
    {
        // Raised on the dictation's thread.
        _ = _uiDispatcher.InvokeAsync(RestartAfterDictation);
    }

    private void RestartAfterDictation()
    {
        if (_restarting || _dictationState.IsActive)
        {
            return;
        }

        // A download that started during the dictation delays the restart again, with its note and a new notice.
        if (_modelStore.IsDownloading)
        {
            _noticeTimer?.Dispose();
            _noticeTimer = null;
            RestartWhenNoDownload();
            return;
        }

        Relaunch();
    }

    private void Relaunch()
    {
        // The restart would end a dictation in progress, and with it the transcript.
        if (_dictationState.IsActive)
        {
            if (!_waitingForDictation)
            {
                _waitingForDictation = true;
                _dictationState.ActiveChanged += OnDictationActiveChanged;
                _logger.LogInformation("The restart waits until the dictation has ended");
            }

            return;
        }

        _restarting = true;
        _dictationState.ActiveChanged -= OnDictationActiveChanged;
        _modelStore.DownloadStateChanged -= OnDownloadStateChanged;
        RelaunchRequested?.Invoke(this, EventArgs.Empty);
    }
}
