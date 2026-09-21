using SharpHook.Data;

namespace Pisum.Transcribe.Recording;

/// <summary>
/// The push-to-talk state machine: turns key-down and key-up events into <see cref="PushToTalkSignal"/>s. Not
/// thread-safe; the caller serializes all calls.
/// </summary>
/// <remarks>
/// A key-down of a key that is already down counts as auto-repeat and is ignored. After a cancel, the hotkey stays
/// suppressed until all hotkey keys are up, so letting go of the hotkey raises nothing.
/// </remarks>
internal sealed class PushToTalkDetector
{
    private readonly IReadOnlySet<KeyCode> _hotkey;
    private readonly HashSet<KeyCode> _downKeys = [];
    private bool _active;
    private bool _suppressed;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="hotkey">The keys that make up the hotkey. Not empty.</param>
    public PushToTalkDetector(IReadOnlySet<KeyCode> hotkey)
    {
        ArgumentOutOfRangeException.ThrowIfZero(hotkey.Count);
        _hotkey = hotkey;
    }

    /// <summary>
    /// The hotkey keys that are currently down. Other keys are never tracked.
    /// </summary>
    public IReadOnlySet<KeyCode> DownKeys => _downKeys;

    /// <summary>
    /// Handles a key-down event.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>
    /// <see cref="PushToTalkSignal.Pressed"/> when the key completes the hotkey, <see cref="PushToTalkSignal.Cancelled"/>
    /// when another key is pressed while the hotkey is active, otherwise <see langword="null"/>.
    /// </returns>
    public PushToTalkSignal? OnKeyDown(KeyCode key)
    {
        if (!_hotkey.Contains(key))
        {
            if (!_active)
            {
                return null;
            }

            _active = false;
            _suppressed = true;
            return PushToTalkSignal.Cancelled;
        }

        if (!_downKeys.Add(key) || _active || _suppressed || _downKeys.Count < _hotkey.Count)
        {
            return null;
        }

        _active = true;
        return PushToTalkSignal.Pressed;
    }

    /// <summary>
    /// Handles a key-up event.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>
    /// <see cref="PushToTalkSignal.Released"/> when a hotkey key is let go while the hotkey is active, otherwise
    /// <see langword="null"/>.
    /// </returns>
    public PushToTalkSignal? OnKeyUp(KeyCode key)
    {
        if (!_downKeys.Remove(key))
        {
            return null;
        }

        if (_downKeys.Count == 0)
        {
            _suppressed = false;
        }

        if (!_active)
        {
            return null;
        }

        _active = false;
        return PushToTalkSignal.Released;
    }

    /// <summary>
    /// Forgets all keys, for when key-up events may have been missed.
    /// </summary>
    /// <returns><see cref="PushToTalkSignal.Cancelled"/> if the hotkey was active, otherwise <see langword="null"/>.</returns>
    public PushToTalkSignal? Reset()
    {
        var wasActive = _active;
        _downKeys.Clear();
        _active = false;
        _suppressed = false;
        return wasActive ? PushToTalkSignal.Cancelled : null;
    }
}
