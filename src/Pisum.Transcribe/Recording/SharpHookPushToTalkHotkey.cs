using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.Tray;
using SharpHook;
using SharpHook.Data;

namespace Pisum.Transcribe.Recording;

/// <summary>
/// The push-to-talk hotkey on a SharpHook keyboard hook. Key events feed a <see cref="PushToTalkDetector"/>; simulated
/// events are dropped. Keys pass through to the focused application unchanged.
/// </summary>
/// <remarks>
/// <para>
/// A non-elevated hook sees no key-up while an elevated window, the UAC prompt or the lock screen has focus. While a
/// hotkey key is down, a timer therefore checks every <see cref="CheckInterval"/> whether the key still reads as down; a
/// key that reads as up on two ticks in a row resets the detector, which cancels an active hotkey.
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
    /// How often a held hotkey key is checked for a missed key-up.
    /// </summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(250);

    // Two readings, so that a normal release, whose key-up event arrives within milliseconds, never turns into a cancel.
    private const int UpReadingsBeforeReset = 2;

    private readonly IGlobalHook _hook;
    private readonly ISettingsStore _settingsStore;
    private readonly ITrayIconService _trayIcon;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SharpHookPushToTalkHotkey> _logger;
    private readonly Func<int, bool> _isKeyDown;
    private readonly Action<Action> _invokeOnUiThread;

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
    /// <param name="settingsStore">The settings store, already loaded.</param>
    /// <param name="trayIcon">The tray icon, for the notification when the hook cannot run.</param>
    /// <param name="timeProvider">The time provider for the missed-release check.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="isKeyDown">
    /// Reads whether a key is down by its Windows virtual-key code, for tests. <see langword="null"/> uses
    /// <c>GetAsyncKeyState</c>.
    /// </param>
    /// <param name="invokeOnUiThread">
    /// Queues an action on the UI thread, for tests. <see langword="null"/> uses the WPF dispatcher.
    /// </param>
    public SharpHookPushToTalkHotkey(IGlobalHook hook,
                                     ISettingsStore settingsStore,
                                     ITrayIconService trayIcon,
                                     TimeProvider timeProvider,
                                     ILogger<SharpHookPushToTalkHotkey> logger,
                                     Func<int, bool>? isKeyDown = null,
                                     Action<Action>? invokeOnUiThread = null)
    {
        _hook = hook;
        _settingsStore = settingsStore;
        _trayIcon = trayIcon;
        _timeProvider = timeProvider;
        _logger = logger;
        _isKeyDown = isKeyDown ?? IsKeyDown;
        _invokeOnUiThread = invokeOnUiThread ?? (action => Application.Current.Dispatcher.InvokeAsync(action));
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

    private static bool IsKeyDown(int virtualKey)
    {
        // Reads as up when UIPI blocks access to the foreground window or another desktop is active.
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

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

            _logger.LogError(exception, "The keyboard hook could not run, push-to-talk is unavailable");
            _invokeOnUiThread(() => _trayIcon.ShowNotification(UnavailableTitle, UnavailableMessage));
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
                // On Windows, the raw code is the left/right-distinguishing virtual-key code.
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
                heldKey.UpReadings = _isKeyDown(heldKey.RawCode) ? 0 : heldKey.UpReadings + 1;
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
