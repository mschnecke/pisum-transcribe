using Microsoft.Extensions.Hosting;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Permissions;
using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Dictation;

/// <summary>
/// The hotkey's availability on macOS (design D4 of add-macos-dictation): the keyboard hook doesn't run while the
/// Accessibility grant isn't in effect, and sees no keys while Secure Event Input is on. Secure input has no change
/// notification, so it is polled while no dictation is in progress.
/// </summary>
internal sealed class MacHotkeyAvailability : IHotkeyAvailability, IHostedService
{
    /// <summary>
    /// How often secure input is read while no dictation is in progress.
    /// </summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IPermissions _permissions;
    private readonly ISecureInput _secureInput;
    private readonly IDictationState _dictationState;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly TimeProvider _timeProvider;

    // Only touched on the UI thread.
    private bool _secureInputOn;
    private ITimer? _pollTimer;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="permissions">Holds whether the Accessibility grant is in effect.</param>
    /// <param name="secureInput">Reads whether secure input is on.</param>
    /// <param name="dictationState">Tells whether a dictation is in progress, which pauses the poll.</param>
    /// <param name="uiDispatcher">Reaches the UI thread, where the state is read and changes are raised.</param>
    /// <param name="timeProvider">The time provider for the poll.</param>
    public MacHotkeyAvailability(IPermissions permissions,
                                 ISecureInput secureInput,
                                 IDictationState dictationState,
                                 IUiDispatcher uiDispatcher,
                                 TimeProvider timeProvider)
    {
        _permissions = permissions;
        _secureInput = secureInput;
        _dictationState = dictationState;
        _uiDispatcher = uiDispatcher;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public HotkeyUnavailableReason? Reason { get; private set; }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Queued before the feedback's first render, because this service is registered before it.
        return _uiDispatcher.InvokeAsync(() =>
        {
            _permissions.AccessibilityInEffectChanged += OnAccessibilityInEffectChanged;
            _dictationState.ActiveChanged += OnDictationActiveChanged;
            Poll();
            _pollTimer = _timeProvider.CreateTimer(_ => _ = _uiDispatcher.InvokeAsync(Poll), null, PollInterval,
                PollInterval);
        });
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() =>
        {
            _permissions.AccessibilityInEffectChanged -= OnAccessibilityInEffectChanged;
            _dictationState.ActiveChanged -= OnDictationActiveChanged;
            _pollTimer?.Dispose();
            _pollTimer = null;
        });
    }

    private void OnAccessibilityInEffectChanged(object? sender, EventArgs e)
    {
        // Raised on the UI thread.
        Update();
    }

    private void OnDictationActiveChanged(object? sender, EventArgs e)
    {
        // Raised on the dictation's thread. Reads secure input again as soon as the dictation has ended.
        _ = _uiDispatcher.InvokeAsync(Poll);
    }

    private void Poll()
    {
        // The tray shows the dictation's phase meanwhile, and the insertion checks secure input itself.
        if (_dictationState.IsActive)
        {
            return;
        }

        _secureInputOn = _secureInput.IsEnabled;
        Update();
    }

    private void Update()
    {
        // Without the grant the hook doesn't run at all, so secure input doesn't matter then.
        HotkeyUnavailableReason? reason = !_permissions.IsAccessibilityInEffect
            ? HotkeyUnavailableReason.AccessibilityNotInEffect
            : _secureInputOn
                ? HotkeyUnavailableReason.SecureInputOn
                : null;
        if (reason == Reason)
        {
            return;
        }

        Reason = reason;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
