using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.Tray;

/// <summary>
/// An <see cref="IPersonalizeKey"/> whose value a test sets, and whose change notification a test fires.
/// </summary>
internal sealed class FakePersonalizeKey(object? value) : IPersonalizeKey
{
    private volatile object? _value = value;
    private volatile EventWaitHandle? _changed;
    private int _armCount;

    /// <summary>
    /// How often the watcher armed the notification.
    /// </summary>
    public int ArmCount => Volatile.Read(ref _armCount);

    public object? ReadSystemUsesLightTheme()
    {
        return _value;
    }

    public bool NotifyOnChange(EventWaitHandle changed)
    {
        _changed = changed;
        Interlocked.Increment(ref _armCount);
        return true;
    }

    /// <summary>
    /// Writes the value and fires the armed notification.
    /// </summary>
    public void Write(object? value)
    {
        _value = value;
        _changed!.Set();
    }

    /// <summary>
    /// Waits until the watcher has armed the notification <paramref name="count"/> times, so that <see cref="Write"/>
    /// can fire it.
    /// </summary>
    public void WaitForArmCount(int count)
    {
        SpinWait.SpinUntil(() => ArmCount >= count, TimeSpan.FromSeconds(5)).ShouldBeTrue();
    }

    public void Dispose()
    {
    }
}
