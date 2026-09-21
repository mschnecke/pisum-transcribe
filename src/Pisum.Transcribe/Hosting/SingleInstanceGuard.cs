namespace Pisum.Transcribe.Hosting;

/// <summary>
/// Allows one running instance per logon session through a named mutex.
/// Mutex ownership belongs to a thread, so acquire and dispose the guard on the same thread.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    /// <summary>
    /// How long a launch waits for a running instance to end. Just over the 5 s exit budget,
    /// so a launch right after <b>Exit</b> starts instead of ending silently.
    /// </summary>
    public static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(6);

    private readonly Mutex _mutex;
    private bool _owned;

    /// <summary>
    /// Initializes a new instance. The mutex is opened, not acquired.
    /// </summary>
    /// <param name="mutexName">The mutex name, such as <c>Local\Pisum.Transcribe.SingleInstance</c>.</param>
    public SingleInstanceGuard(string mutexName)
    {
        _mutex = new Mutex(false, mutexName);
    }

    /// <summary>
    /// Waits to own the mutex.
    /// </summary>
    /// <param name="timeout">The maximum wait.</param>
    /// <returns><see langword="true"/> if this instance may start; <see langword="false"/> if another instance is running.</returns>
    public bool TryAcquire(TimeSpan timeout)
    {
        try
        {
            _owned = _mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner ended without releasing the mutex, for example after a crash.
            _owned = true;
        }

        return _owned;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_owned)
        {
            _mutex.ReleaseMutex();
            _owned = false;
        }

        _mutex.Dispose();
    }
}
