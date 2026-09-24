using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Notifications;
using Pisum.Transcribe.Settings;
using SharpHook;
using SharpHook.Data;

namespace Pisum.Transcribe.Recording;

/// <summary>
/// The push-to-talk hotkey on a SharpHook keyboard hook. Key events feed a <see cref="PushToTalkDetector"/>; simulated
/// events are dropped. Keys pass through to the focused application unchanged.
/// </summary>
/// <remarks>
/// <para>
/// A non-elevated hook sees no key-up while an elevated window, the UAC prompt or the lock screen has focus, and on
/// macOS while secure input hides key events. While a hotkey key is down, a timer therefore checks every
/// <see cref="CheckInterval"/> whether the key still reads as held through <see cref="IHotkeyKeyState"/>; a key that
/// reads as up on two ticks in a row resets the detector, which cancels an active hotkey.
/// </para>
/// <para>
/// The hook runs only while <see cref="IHookAccess"/> allows it. When its access is revoked while it runs, the hook ends
/// with <see cref="UioHookResult.ErrorAxApiRevoked"/>, which is shown and reported.
/// </para>
/// <para>
/// The hook sees every key on the system. Only hotkey keys are kept, and only as long as they are down; logs name the
/// signal, never a key. While suspended, key events are passed on as <see cref="RawKey"/> and not kept.
/// </para>
/// </remarks>
internal sealed class SharpHookPushToTalkHotkey : IPushToTalkHotkey, IHostedService
{
    /// <summary>
    /// The notification title when the hook could not run.
    /// </summary>
    public const string UnavailableTitle = "Push-to-talk unavailable";

    /// <summary>
    /// The notification text when the hook could not run.
    /// </summary>
    public const string UnavailableMessage = "The push-to-talk key could not be set up. Details are in the log.";

    /// <summary>
    /// The notification title when the hook's Accessibility access was revoked while it ran.
    /// </summary>
    public const string RevokedTitle = "Push-to-talk stopped";

    /// <summary>
    /// The notification text when the hook's Accessibility access was revoked while it ran.
    /// </summary>
    public const string RevokedMessage =
        "Accessibility access for Pisum Transcribe was turned off. Choose Set up Pisum Transcribe… in the menu to allow it again.";

    /// <summary>
    /// How often a held hotkey key is checked for a missed key-up.
    /// </summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(250);

    // Two readings, so that a normal release, whose key-up event arrives within milliseconds, never turns into a cancel.
    private const int UpReadingsBeforeReset = 2;

    private readonly IGlobalHook _hook;
    private readonly IHotkeyKeyState _keyState;
    private readonly IHookAccess _hookAccess;
    private readonly ISettingsStore _settingsStore;
    private readonly INotifier _notifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SharpHookPushToTalkHotkey> _logger;

    // Guards the detector, the held keys and the timer. Hook handlers and the timer run on different threads, and signals
    // are raised inside the lock, so consumers see them in order.
    private readonly Lock _lock = new();
    private readonly Dictionary<KeyCode, HeldKey> _heldKeys = [];
    private PushToTalkDetector? _detector;
    private ITimer? _checkTimer;
    private bool _suspended;
    private bool _stopped;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="hook">The global hook, not yet running. Disposed on stop.</param>
    /// <param name="keyState">Reads whether a held hotkey key is still observably held.</param>
    /// <param name="hookAccess">Whether the hook may run, and where a revoke is reported.</param>
    /// <param name="settingsStore">The settings store, already loaded.</param>
    /// <param name="notifier">Shows the notification when the hook cannot run.</param>
    /// <param name="timeProvider">The time provider for the missed-release check.</param>
    /// <param name="logger">The logger.</param>
    public SharpHookPushToTalkHotkey(IGlobalHook hook,
                                     IHotkeyKeyState keyState,
                                     IHookAccess hookAccess,
                                     ISettingsStore settingsStore,
                                     INotifier notifier,
                                     TimeProvider timeProvider,
                                     ILogger<SharpHookPushToTalkHotkey> logger)
    {
        _hook = hook;
        _keyState = keyState;
        _hookAccess = hookAccess;
        _settingsStore = settingsStore;
        _notifier = notifier;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler? Pressed;

    /// <inheritdoc />
    public event EventHandler? Released;

    /// <inheritdoc />
    public event EventHandler? Cancelled;

    /// <inheritdoc />
    public event EventHandler<RawKeyEventArgs>? RawKey;

    /// <inheritdoc />
    public void SetHotkey(IReadOnlySet<KeyCode> hotkey)
    {
        var detector = new PushToTalkDetector(hotkey);
        lock (_lock)
        {
            var signal = _detector?.Reset();
            _detector = detector;
            _heldKeys.Clear();
            UpdateCheckTimer();
            Raise(signal);
        }
    }

    /// <inheritdoc />
    public void Suspend()
    {
        lock (_lock)
        {
            if (_suspended)
            {
                return;
            }

            _suspended = true;
            var signal = _detector?.Reset();
            _heldKeys.Clear();
            UpdateCheckTimer();
            Raise(signal);
        }
    }

    /// <inheritdoc />
    public void Resume()
    {
        lock (_lock)
        {
            _suspended = false;
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var hotkey = HotkeyParser.Parse(_settingsStore.Current.Recording.Hotkey, _logger);
        lock (_lock)
        {
            _detector = new PushToTalkDetector(hotkey);
        }

        if (!_hookAccess.IsAllowed)
        {
            // The setup window and its menu item already say that the grant is missing.
            _logger.LogInformation("Push-to-talk waits for the Accessibility grant, the keyboard hook isn't started");
            return Task.CompletedTask;
        }

        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
        _ = RunHookAsync();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _stopped = true;
            _checkTimer?.Dispose();
            _checkTimer = null;
            _heldKeys.Clear();
        }

        _hook.KeyPressed -= OnKeyPressed;
        _hook.KeyReleased -= OnKeyReleased;
        _hook.Dispose();
        return Task.CompletedTask;
    }

    private async Task RunHookAsync()
    {
        try
        {
            // Keyboard only: no low-level mouse hook, so mouse input never passes through the process.
            await _hook.RunAsync(GlobalHookType.Keyboard, true).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            lock (_lock)
            {
                if (_stopped)
                {
                    return;
                }
            }

            if (exception is HookException {Result: UioHookResult.ErrorAxApiRevoked})
            {
                _logger.LogWarning("Accessibility access was revoked, push-to-talk stopped");
                _notifier.Show(RevokedTitle, RevokedMessage);
                _hookAccess.OnRevoked();
                return;
            }

            _logger.LogError(exception, "The keyboard hook could not run, push-to-talk is unavailable");
            _notifier.Show(UnavailableTitle, UnavailableMessage);
        }
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        // Covers this application's paste keystrokes, other applications' SendInput and key remapping tools.
        if (e.IsEventSimulated)
        {
            return;
        }

        var key = e.Data.KeyCode;
        lock (_lock)
        {
            if (_stopped || _detector is null)
            {
                return;
            }

            if (_suspended)
            {
                RawKey?.Invoke(this, new RawKeyEventArgs(key, true));
                return;
            }

            var signal = _detector.OnKeyDown(key);
            if (_detector.DownKeys.Contains(key))
            {
                // The raw code is the left/right-distinguishing virtual-key code on Windows, and the key code on macOS.
                _heldKeys[key] = new HeldKey(e.Data.RawCode);
            }

            UpdateCheckTimer();
            Raise(signal);
        }
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (e.IsEventSimulated)
        {
            return;
        }

        var key = e.Data.KeyCode;
        lock (_lock)
        {
            if (_stopped || _detector is null)
            {
                return;
            }

            if (_suspended)
            {
                RawKey?.Invoke(this, new RawKeyEventArgs(key, false));
                return;
            }

            var signal = _detector.OnKeyUp(key);
            _heldKeys.Remove(key);
            UpdateCheckTimer();
            Raise(signal);
        }
    }

    private void CheckHeldKeys()
    {
        lock (_lock)
        {
            if (_stopped || _detector is null || _heldKeys.Count == 0)
            {
                return;
            }

            var missedRelease = false;
            foreach (var heldKey in _heldKeys.Values)
            {
                heldKey.UpReadings = _keyState.IsHeld(heldKey.RawCode) ? 0 : heldKey.UpReadings + 1;
                missedRelease |= heldKey.UpReadings >= UpReadingsBeforeReset;
            }

            if (!missedRelease)
            {
                return;
            }

            _logger.LogDebug("A push-to-talk key reads as up without a key-up event, resetting the hotkey state");
            var signal = _detector.Reset();
            _heldKeys.Clear();
            UpdateCheckTimer();
            Raise(signal);
        }
    }

    private void UpdateCheckTimer()
    {
        if (_heldKeys.Count > 0)
        {
            _checkTimer ??= _timeProvider.CreateTimer(_ => CheckHeldKeys(), null, CheckInterval, CheckInterval);
        }
        else
        {
            _checkTimer?.Dispose();
            _checkTimer = null;
        }
    }

    private void Raise(PushToTalkSignal? signal)
    {
        if (signal is not { } value)
        {
            return;
        }

        _logger.LogDebug("Push-to-talk {Signal}", value);
        var handler = value switch
        {
            PushToTalkSignal.Pressed => Pressed,
            PushToTalkSignal.Released => Released,
            _ => Cancelled,
        };
        handler?.Invoke(this, EventArgs.Empty);
    }

    private sealed class HeldKey(ushort rawCode)
    {
        public int RawCode { get; } = rawCode;

        public int UpReadings { get; set; }
    }
}
