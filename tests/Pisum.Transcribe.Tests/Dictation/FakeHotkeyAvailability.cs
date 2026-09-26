using Pisum.Transcribe.Dictation;

namespace Pisum.Transcribe.Tests.Dictation;

/// <summary>
/// A hotkey availability whose reason the test sets, raising <see cref="Changed"/> on the calling thread.
/// </summary>
internal sealed class FakeHotkeyAvailability : IHotkeyAvailability
{
    private HotkeyUnavailableReason? _reason;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public HotkeyUnavailableReason? Reason
    {
        get => _reason;
        set
        {
            _reason = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Gets whether anything is subscribed to <see cref="Changed"/>.
    /// </summary>
    public bool HasSubscribers => Changed is not null;
}
