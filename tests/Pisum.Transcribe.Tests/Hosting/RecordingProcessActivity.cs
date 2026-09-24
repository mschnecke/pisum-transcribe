using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

/// <summary>
/// Records the activities that are begun and how many of them are still running. Safe on any thread.
/// </summary>
internal sealed class RecordingProcessActivity : IProcessActivity
{
    private readonly Lock _gate = new();
    private readonly List<string> _begun = [];
    private int _running;

    /// <summary>
    /// Gets the reasons of the activities begun so far, in order.
    /// </summary>
    public IReadOnlyList<string> Begun
    {
        get
        {
            lock (_gate)
            {
                return [.. _begun];
            }
        }
    }

    /// <summary>
    /// Gets the number of activities begun and not yet ended.
    /// </summary>
    public int Running
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    /// <inheritdoc />
    public IDisposable Begin(string reason)
    {
        lock (_gate)
        {
            _begun.Add(reason);
            _running++;
        }

        return new Activity(this);
    }

    private void End()
    {
        lock (_gate)
        {
            _running--;
        }
    }

    private sealed class Activity(RecordingProcessActivity owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            // Ending an activity twice would hide a missing end elsewhere.
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.End();
            }
        }
    }
}
