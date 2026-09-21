using Pisum.Transcribe.Dictation;
using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.Dictation;

/// <summary>
/// Records feedback calls in order, from any thread.
/// </summary>
internal sealed class FakeDictationFeedback : IDictationFeedback
{
    public const string Starting = nameof(Starting);
    public const string Recording = nameof(Recording);
    public const string Transcribing = nameof(Transcribing);
    public const string Busy = nameof(Busy);
    public const string NoSpeech = nameof(NoSpeech);
    public const string Idle = nameof(Idle);

    private readonly Lock _lock = new();
    private readonly List<string> _calls = [];
    private readonly List<InsertionTarget> _startingTargets = [];
    private readonly List<Notification> _notifications = [];

    /// <summary>
    /// The <c>Show*</c> calls in order, such as <see cref="Starting"/>.
    /// </summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return _calls.ToList();
            }
        }
    }

    public IReadOnlyList<InsertionTarget> StartingTargets
    {
        get
        {
            lock (_lock)
            {
                return _startingTargets.ToList();
            }
        }
    }

    public IReadOnlyList<Notification> Notifications
    {
        get
        {
            lock (_lock)
            {
                return _notifications.ToList();
            }
        }
    }

    public event EventHandler? CancelRequested;

    public int Count(string call)
    {
        return Calls.Count(c => c == call);
    }

    /// <summary>
    /// Raises <see cref="CancelRequested"/>, as choosing <b>Cancel transcription</b> in the tray menu does.
    /// </summary>
    public void RequestCancel()
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ShowStarting(InsertionTarget target)
    {
        lock (_lock)
        {
            _startingTargets.Add(target);
            _calls.Add(Starting);
        }
    }

    public void ShowRecording()
    {
        Add(Recording);
    }

    public void ShowTranscribing()
    {
        Add(Transcribing);
    }

    public void ShowBusy()
    {
        Add(Busy);
    }

    public void ShowNoSpeech()
    {
        Add(NoSpeech);
    }

    public void ShowIdle()
    {
        Add(Idle);
    }

    public void Notify(string title, string message)
    {
        lock (_lock)
        {
            _notifications.Add(new Notification(title, message));
        }
    }

    private void Add(string call)
    {
        lock (_lock)
        {
            _calls.Add(call);
        }
    }
}

internal sealed record Notification(string Title, string Message);
