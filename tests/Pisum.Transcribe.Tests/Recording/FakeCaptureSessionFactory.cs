using Pisum.Transcribe.Recording;

namespace Pisum.Transcribe.Tests.Recording;

/// <summary>
/// Creates <see cref="FakeCaptureSession"/>s and keeps them for inspection.
/// </summary>
internal sealed class FakeCaptureSessionFactory : ICaptureSessionFactory
{
    private readonly Lock _lock = new();
    private readonly List<FakeCaptureSession> _sessions = [];

    /// <summary>
    /// Thrown by <see cref="CreateAsync"/> instead of creating a session.
    /// </summary>
    public Exception? CreateException { get; set; }

    /// <summary>
    /// When set, <see cref="CreateAsync"/> waits for it before creating a session.
    /// </summary>
    public TaskCompletionSource? CreateGate { get; set; }

    /// <summary>
    /// Applied to each new session before it is returned.
    /// </summary>
    public Action<FakeCaptureSession>? Configure { get; set; }

    public IReadOnlyList<FakeCaptureSession> Sessions
    {
        get
        {
            lock (_lock)
            {
                return _sessions.ToList();
            }
        }
    }

    public FakeCaptureSession LastSession => Sessions[^1];

    public async Task<ICaptureSession> CreateAsync(CancellationToken cancellationToken)
    {
        if (CreateGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        if (CreateException is { } exception)
        {
            throw exception;
        }

        var session = new FakeCaptureSession();
        Configure?.Invoke(session);
        lock (_lock)
        {
            _sessions.Add(session);
        }

        return session;
    }
}

/// <summary>
/// A capture session driven by the test: <see cref="Deliver"/> raises a packet and <see cref="Fail"/> a capture error.
/// </summary>
internal sealed class FakeCaptureSession : ICaptureSession
{
    private int _startCalls;
    private int _stopCalls;
    private int _disposeCalls;

    public event SamplesAvailableHandler? SamplesAvailable;

    public event EventHandler<Exception?>? Stopped;

    /// <summary>
    /// Thrown by <see cref="Start"/>.
    /// </summary>
    public Exception? StartException { get; set; }

    /// <summary>
    /// When set, <see cref="StopAsync"/> waits for it.
    /// </summary>
    public TaskCompletionSource? StopGate { get; set; }

    /// <summary>
    /// Runs inside <see cref="StopAsync"/>, before it completes.
    /// </summary>
    public Action? OnStop { get; set; }

    public int StartCalls => Volatile.Read(ref _startCalls);

    public int StopCalls => Volatile.Read(ref _stopCalls);

    public int DisposeCalls => Volatile.Read(ref _disposeCalls);

    public bool IsDisposed => DisposeCalls > 0;

    public void Start()
    {
        Interlocked.Increment(ref _startCalls);
        if (StartException is { } exception)
        {
            throw exception;
        }
    }

    public async Task StopAsync()
    {
        Interlocked.Increment(ref _stopCalls);
        OnStop?.Invoke();
        if (StopGate is { } gate)
        {
            await gate.Task;
        }
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCalls);
        return ValueTask.CompletedTask;
    }

    public void Deliver(float[] samples, bool silent = false)
    {
        SamplesAvailable?.Invoke(samples, silent);
    }

    public void Fail(Exception? exception)
    {
        Stopped?.Invoke(this, exception);
    }
}
