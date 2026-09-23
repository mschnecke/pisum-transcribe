using Microsoft.Extensions.Logging;

namespace Pisum.Transcribe.Tray;

/// <summary>
/// Follows the taskbar's mode, the value <c>SystemUsesLightTheme</c>, through a registry change notification. A
/// missing value counts as dark.
/// </summary>
internal sealed class TaskbarModeWatcher : ITaskbarModeWatcher, IDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(1);

    private readonly IPersonalizeKey _key;
    private readonly ILogger<TaskbarModeWatcher> _logger;
    private readonly AutoResetEvent _keyChanged = new(false);
    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread _thread;
    private volatile TaskbarMode _current;

    /// <summary>
    /// Initializes a new instance, reads the mode and starts watching it.
    /// </summary>
    /// <param name="key">The registry key that holds the mode.</param>
    /// <param name="logger">The logger.</param>
    public TaskbarModeWatcher(IPersonalizeKey key, ILogger<TaskbarModeWatcher> logger)
    {
        _key = key;
        _logger = logger;
        _current = Read();

        // A background thread, so it can't keep the process alive if Dispose doesn't stop it in time.
        _thread = new Thread(Watch) {IsBackground = true, Name = "Taskbar mode"};
        _thread.Start();
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public TaskbarMode Current => _current;

    /// <summary>
    /// Whether the thread still watches the key, for tests.
    /// </summary>
    internal bool IsWatching => _thread.IsAlive;

    /// <inheritdoc />
    public void Dispose()
    {
        _stop.Set();
        if (_thread.Join(StopTimeout))
        {
            _key.Dispose();
            _keyChanged.Dispose();
            _stop.Dispose();
        }
    }

    private void Watch()
    {
        WaitHandle[] handles = [_keyChanged, _stop];
        while (true)
        {
            // A notification fires only once, so it is armed again before each wait. Reading after arming catches a
            // change that happened before.
            if (!_key.NotifyOnChange(_keyChanged))
            {
                _logger.LogWarning("The taskbar mode can't be watched. The tray icon keeps the mode {Mode}.", _current);
                return;
            }

            Update();
            if (WaitHandle.WaitAny(handles) == 1)
            {
                return;
            }
        }
    }

    private void Update()
    {
        // The key also changes for the accent color and transparency, so only a flip of the mode is raised.
        var mode = Read();
        if (mode == _current)
        {
            return;
        }

        _current = mode;
        _logger.LogInformation("The taskbar mode changed to {Mode}.", mode);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private TaskbarMode Read()
    {
        return _key.ReadSystemUsesLightTheme() is int value and not 0 ? TaskbarMode.Light : TaskbarMode.Dark;
    }
}
