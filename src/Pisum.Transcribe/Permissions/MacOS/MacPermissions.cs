using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Notifications;

namespace Pisum.Transcribe.Permissions;

/// <summary>
/// The permissions on macOS (design D2 of add-macos-setup): Accessibility through the Accessibility C API, the
/// microphone and the pasteboard through the Swift helper, and notifications through the notification center.
/// </summary>
internal sealed class MacPermissions : IPermissions
{
    private readonly MacNativeLibrary _library;
    private readonly INotificationCenter _notificationCenter;

    /// <summary>
    /// Initializes a new instance and reads whether the process has the Accessibility grant, so create it at startup.
    /// </summary>
    /// <param name="library">The Swift helper.</param>
    /// <param name="notificationCenter">The notification center.</param>
    public MacPermissions(MacNativeLibrary library, INotificationCenter notificationCenter)
    {
        _library = library;
        _notificationCenter = notificationCenter;
        IsAccessibilityGrantedAtStart = CoreFoundation.IsProcessTrusted();
    }

    /// <inheritdoc />
    public bool IsAccessibilityGrantedAtStart { get; }

    /// <inheritdoc />
    public PermissionState GetState(Permission permission)
    {
        return permission switch
        {
            Permission.Accessibility =>
                CoreFoundation.IsProcessTrusted() ? PermissionState.Granted : PermissionState.NotDetermined,
            Permission.Microphone => _library.IsAvailable
                ? ToMicrophoneState(PisumMac.MicrophoneStatus())
                : PermissionState.NotDetermined,
            Permission.PasteFromOtherApps => _library.IsAvailable
                ? ToPasteState(PisumMac.PasteboardAccessBehavior())
                : PermissionState.NotDetermined,
            _ => throw new ArgumentOutOfRangeException(nameof(permission), permission,
                "Read the notification state with GetNotificationsStateAsync."),
        };
    }

    /// <inheritdoc />
    public Task<PermissionState> GetNotificationsStateAsync()
    {
        var completed = new TaskCompletionSource<PermissionState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var status = _notificationCenter.ReadAuthorization(authorization => completed.SetResult(authorization switch
        {
            NotificationAuthorization.Authorized => PermissionState.Granted,
            NotificationAuthorization.Denied => PermissionState.Denied,
            _ => PermissionState.NotDetermined,
        }));
        if (status == NotificationStatus.Unavailable)
        {
            completed.SetResult(PermissionState.Denied);
        }

        return completed.Task;
    }

    /// <inheritdoc />
    public void PromptForAccessibility()
    {
        CoreFoundation.PromptForAccessibility();
    }

    /// <inheritdoc />
    public Task<PermissionState> RequestMicrophoneAsync()
    {
        if (!_library.IsAvailable)
        {
            return Task.FromResult(PermissionState.NotDetermined);
        }

        var completed = new TaskCompletionSource<PermissionState>(TaskCreationOptions.RunContinuationsAsynchronously);
        PisumMac.MicrophoneRequest(PisumMac.Callback,
            PisumMac.CreateContext(status => completed.SetResult(ToMicrophoneState(status))));
        return completed.Task;
    }

    /// <inheritdoc />
    public Task RequestNotificationsAsync()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_notificationCenter.RequestAuthorization(_ => completed.SetResult()) == NotificationStatus.Unavailable)
        {
            completed.SetResult();
        }

        return completed.Task;
    }

    /// <inheritdoc />
    public void ProbePasteboard()
    {
        if (_library.IsAvailable)
        {
            PisumMac.PasteboardProbe();
        }
    }

    /// <summary>
    /// Maps the helper's microphone status to a state.
    /// </summary>
    /// <param name="status">0 not asked yet, 1 restricted, 2 denied, 3 allowed.</param>
    /// <returns>The state.</returns>
    internal static PermissionState ToMicrophoneState(int status)
    {
        return status switch
        {
            3 => PermissionState.Granted,
            // 1 restricted, for example by a device management profile, and 2 denied.
            1 or 2 => PermissionState.Denied,
            _ => PermissionState.NotDetermined,
        };
    }

    /// <summary>
    /// Maps the helper's pasteboard access behavior to a state (design D6 of add-macos-setup).
    /// </summary>
    /// <param name="behavior">-1 before macOS 15.4, 0 default, 1 asks each time, 2 always allowed, 3 always denied.</param>
    /// <returns>The state.</returns>
    internal static PermissionState ToPasteState(int behavior)
    {
        return behavior switch
        {
            0 => PermissionState.NotDetermined,
            1 => PermissionState.AsksEachTime,
            3 => PermissionState.Denied,
            // -1 before macOS 15.4, which has no pasteboard privacy, and 2 always allowed.
            _ => PermissionState.Granted,
        };
    }
}
