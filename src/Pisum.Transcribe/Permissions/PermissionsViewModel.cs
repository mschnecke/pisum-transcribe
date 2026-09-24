using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Permissions;

/// <summary>
/// The permission part of the setup window on macOS: one row per permission, which follows the permission's state
/// while the window is open. One instance serves every setup window of the process. Use it on the UI thread.
/// </summary>
internal sealed partial class PermissionsViewModel : ObservableObject
{
    /// <summary>
    /// How often the rows are refreshed while the window is open, so a change in System Settings shows within 2 s.
    /// </summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// System Settings → Privacy &amp; Security → Accessibility.
    /// </summary>
    public const string AccessibilitySettingsUrl =
        "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility";

    /// <summary>
    /// System Settings → Privacy &amp; Security → Microphone.
    /// </summary>
    public const string MicrophoneSettingsUrl =
        "x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone";

    /// <summary>
    /// System Settings → Privacy &amp; Security → Paste from Other Apps.
    /// </summary>
    public const string PasteSettingsUrl = "x-apple.systempreferences:com.apple.preference.security?Privacy_Pasteboard";

    private const string AllowText = "Allow…";
    private const string OpenSettingsText = "Open Settings…";

    private readonly IPermissions _permissions;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly Action<string> _openUrl;

    private bool _accessibilityPrompted;
    private bool _notificationsRequested;
    private bool _pasteboardProbed;
    private ITimer? _refreshTimer;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="permissions">Reads and requests the permissions.</param>
    /// <param name="uiDispatcher">Reaches the UI thread from the refresh timer and the pasteboard probe.</param>
    /// <param name="timeProvider">The time provider for the refresh.</param>
    /// <param name="openUrl">Opens a System Settings link.</param>
    public PermissionsViewModel(IPermissions permissions,
                                IUiDispatcher uiDispatcher,
                                TimeProvider timeProvider,
                                Action<string> openUrl)
    {
        _permissions = permissions;
        _uiDispatcher = uiDispatcher;
        _timeProvider = timeProvider;
        _openUrl = openUrl;

        Accessibility = new PermissionRowViewModel(Permission.Accessibility, "Accessibility",
            "For the push-to-talk key and to insert the text.", true,
            state => state == PermissionState.Granted ? null : AllowText, AllowAccessibility);
        Microphone = new PermissionRowViewModel(Permission.Microphone, "Microphone",
            "To record while you hold the push-to-talk key.", true,
            state => state == PermissionState.Granted ? null : AllowText, AllowMicrophoneAsync);
        Notifications = new PermissionRowViewModel(Permission.Notifications, "Notifications",
            "To tell you when a model is ready or something went wrong.", false,
            _ => null, () => Task.CompletedTask);
        PasteFromOtherApps = new PermissionRowViewModel(Permission.PasteFromOtherApps, "Paste from other apps",
            "To insert the text through the clipboard.", false,
            state => state is PermissionState.AsksEachTime or PermissionState.Denied ? OpenSettingsText : null,
            () => OpenUrl(PasteSettingsUrl));
        Rows = [Accessibility, Microphone, Notifications, PasteFromOtherApps];

        Accessibility.PropertyChanged += OnRequiredRowChanged;
        Microphone.PropertyChanged += OnRequiredRowChanged;
        _permissions.AccessibilityInEffectChanged += (_, _) => OnPropertyChanged(nameof(AreRequiredGranted));
        ReadStates();
    }

    /// <summary>
    /// The Accessibility row.
    /// </summary>
    public PermissionRowViewModel Accessibility { get; }

    /// <summary>
    /// The Microphone row.
    /// </summary>
    public PermissionRowViewModel Microphone { get; }

    /// <summary>
    /// The Notifications row.
    /// </summary>
    public PermissionRowViewModel Notifications { get; }

    /// <summary>
    /// The Paste from other apps row.
    /// </summary>
    public PermissionRowViewModel PasteFromOtherApps { get; }

    /// <summary>
    /// The rows in the order the window shows them.
    /// </summary>
    public IReadOnlyList<PermissionRowViewModel> Rows { get; }

    /// <summary>
    /// Whether both required permissions are granted and in effect, as the rows last showed them. An Accessibility
    /// grant made while the process runs, also after a revoke, counts only after the restart.
    /// </summary>
    public bool AreRequiredGranted => _permissions.IsAccessibilityInEffect
                                      && Accessibility.State == PermissionState.Granted
                                      && Microphone.State == PermissionState.Granted;

    /// <summary>
    /// Reads now whether both required permissions are granted, for a check outside the window such as the menu.
    /// Unlike <see cref="AreRequiredGranted"/>, an Accessibility grant made while the process runs counts at once,
    /// because <c>RelaunchService</c> restarts the application right after it.
    /// </summary>
    /// <returns><see langword="true"/> if Accessibility and the microphone are granted.</returns>
    public bool ReadRequiredGranted()
    {
        return _permissions.GetState(Permission.Accessibility) == PermissionState.Granted
               && _permissions.GetState(Permission.Microphone) == PermissionState.Granted;
    }

    /// <summary>
    /// Starts following the permissions when the setup window opens: refreshes the rows every
    /// <see cref="RefreshInterval"/>, asks for notifications when the user wasn't asked yet, and reads the pasteboard
    /// once when its access is at the default. Each request runs once per process.
    /// </summary>
    /// <returns>A task that completes when the first refresh and the requests have run.</returns>
    public Task OpenAsync()
    {
        _refreshTimer ??= _timeProvider.CreateTimer(_ => _ = _uiDispatcher.InvokeAsync(() => _ = RefreshAsync()),
            null, RefreshInterval, RefreshInterval);
        return Task.WhenAll(RequestNotificationsAsync(), ProbePasteboardAsync());
    }

    /// <summary>
    /// Stops the refresh when the setup window closes.
    /// </summary>
    public void Close()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }

    /// <summary>
    /// Reads the state of every permission into the rows.
    /// </summary>
    /// <returns>A task that completes when the notification state was read too.</returns>
    public async Task RefreshAsync()
    {
        ReadStates();
        Notifications.State = await _permissions.GetNotificationsStateAsync();
    }

    private void ReadStates()
    {
        Accessibility.State = _permissions.GetState(Permission.Accessibility);
        Microphone.State = _permissions.GetState(Permission.Microphone);
        PasteFromOtherApps.State = _permissions.GetState(Permission.PasteFromOtherApps);
    }

    private async Task RequestNotificationsAsync()
    {
        await RefreshAsync();
        if (Notifications.State != PermissionState.NotDetermined || _notificationsRequested)
        {
            return;
        }

        _notificationsRequested = true;
        await _permissions.RequestNotificationsAsync();
        await _uiDispatcher.InvokeAsync(() => _ = RefreshAsync());
    }

    private async Task ProbePasteboardAsync()
    {
        if (PasteFromOtherApps.State != PermissionState.NotDetermined || _pasteboardProbed)
        {
            return;
        }

        // On a thread-pool thread, because macOS's alert blocks the reading thread until the user answers.
        _pasteboardProbed = true;
        await Task.Run(_permissions.ProbePasteboard);
        await _uiDispatcher.InvokeAsync(ReadStates);
    }

    private Task AllowAccessibility()
    {
        // macOS shows its prompt only once, so from the second time the button also opens System Settings.
        _permissions.PromptForAccessibility();
        if (_accessibilityPrompted)
        {
            _openUrl(AccessibilitySettingsUrl);
        }

        _accessibilityPrompted = true;
        return Task.CompletedTask;
    }

    private async Task AllowMicrophoneAsync()
    {
        if (Microphone.State == PermissionState.NotDetermined)
        {
            var state = await _permissions.RequestMicrophoneAsync();
            await _uiDispatcher.InvokeAsync(() => Microphone.State = state);
        }
        else
        {
            _openUrl(MicrophoneSettingsUrl);
        }
    }

    private Task OpenUrl(string url)
    {
        _openUrl(url);
        return Task.CompletedTask;
    }

    private void OnRequiredRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PermissionRowViewModel.State))
        {
            OnPropertyChanged(nameof(AreRequiredGranted));
        }
    }
}
