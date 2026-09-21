using Pisum.Transcribe.Settings;

namespace Pisum.Transcribe.Tests.SettingsWindow;

/// <summary>
/// A settings store in memory that behaves like <see cref="JsonSettingsStore"/>: a save makes the settings current and
/// raises <see cref="Changed"/>.
/// </summary>
internal sealed class FakeSettingsStore(AppSettings current) : ISettingsStore
{
    public event EventHandler<SettingsChangedEventArgs>? Changed;

    public AppSettings Current { get; private set; } = current;

    /// <summary>
    /// The saved settings in order.
    /// </summary>
    public List<AppSettings> Saves { get; } = [];

    /// <summary>
    /// Thrown by the next saves instead of saving, when set.
    /// </summary>
    public Exception? SaveException { get; set; }

    public void Load()
    {
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (SaveException is not null)
        {
            return Task.FromException(SaveException);
        }

        var previous = Current;
        Current = settings;
        Saves.Add(settings);
        Changed?.Invoke(this, new SettingsChangedEventArgs(previous, settings));
        return Task.CompletedTask;
    }
}
