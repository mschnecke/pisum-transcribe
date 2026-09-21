using Microsoft.Extensions.Logging;

namespace Pisum.Transcribe.Tests;

/// <summary>
/// A logger that records every entry, from any thread.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly Lock _lock = new();
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToList();
            }
        }
    }

    public void Log<TState>(LogLevel logLevel,
                            EventId eventId,
                            TState state,
                            Exception? exception,
                            Func<TState, Exception?, string> formatter)
    {
        var properties = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
        lock (_lock)
        {
            _entries.Add(new LogEntry(logLevel, formatter(state, exception), properties, exception));
        }
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }
}

/// <summary>
/// One recorded log entry.
/// </summary>
/// <param name="Message">The formatted message.</param>
/// <param name="Properties">The structured properties, including the message template as <c>{OriginalFormat}</c>.</param>
public sealed record LogEntry(
    LogLevel Level,
    string Message,
    IReadOnlyList<KeyValuePair<string, object?>> Properties,
    Exception? Exception);
