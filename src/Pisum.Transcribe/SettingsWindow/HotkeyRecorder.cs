using System.Collections.Frozen;
using SharpHook.Data;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// The state of a hotkey recording.
/// </summary>
internal enum HotkeyRecordingState
{
    /// <summary>
    /// Keys are being recorded.
    /// </summary>
    Recording,

    /// <summary>
    /// A valid hotkey was captured.
    /// </summary>
    Captured,

    /// <summary>
    /// A hotkey without a modifier or function key was captured and rejected.
    /// </summary>
    Rejected,

    /// <summary>
    /// The recording was cancelled with Esc or <see cref="HotkeyRecorder.Cancel"/>.
    /// </summary>
    Cancelled,
}

/// <summary>
/// Records a new push-to-talk hotkey from raw key events: the keys the user holds together, captured when all of them
/// are released. Not thread-safe; the caller serializes all calls.
/// </summary>
/// <remarks>
/// When the user holds different combinations during one recording, the first largest one wins. A key-up of a key that
/// was down before the recording started is ignored, such as the Enter that chose <b>Change…</b>.
/// </remarks>
internal sealed class HotkeyRecorder
{
    /// <summary>
    /// The validation message for a rejected hotkey.
    /// </summary>
    public const string RejectedMessage =
        "The hotkey must include Ctrl, Alt, Shift, the Windows key or a function key F1–F24.";

    // By name, because the key codes of F1-F24 are not contiguous.
    private static readonly FrozenSet<KeyCode> RequiredKeys =
    [
        KeyCode.VcLeftControl, KeyCode.VcRightControl, KeyCode.VcLeftAlt, KeyCode.VcRightAlt, KeyCode.VcLeftShift,
        KeyCode.VcRightShift, KeyCode.VcLeftMeta, KeyCode.VcRightMeta,
        ..Enumerable.Range(1, 24).Select(number => Enum.Parse<KeyCode>($"VcF{number}")),
    ];

    private readonly HashSet<KeyCode> _held = [];
    private HashSet<KeyCode> _keys = [];

    /// <summary>
    /// The state of the recording.
    /// </summary>
    public HotkeyRecordingState State { get; private set; } = HotkeyRecordingState.Recording;

    /// <summary>
    /// The captured keys once <see cref="State"/> is <see cref="HotkeyRecordingState.Captured"/> or
    /// <see cref="HotkeyRecordingState.Rejected"/>.
    /// </summary>
    public IReadOnlySet<KeyCode> Keys => _keys;

    /// <summary>
    /// Checks whether keys may form a hotkey: at least one of Ctrl, Alt, Shift, the Windows key or F1–F24.
    /// </summary>
    /// <param name="keys">The keys.</param>
    /// <returns><see langword="true"/> if the keys may form a hotkey.</returns>
    public static bool IsValid(IEnumerable<KeyCode> keys)
    {
        return keys.Any(RequiredKeys.Contains);
    }

    /// <summary>
    /// Handles a raw key event. Has no effect once the recording has ended.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="isPressed"><see langword="true"/> for a key-down, <see langword="false"/> for a key-up.</param>
    /// <returns>The state after the event.</returns>
    public HotkeyRecordingState OnKey(KeyCode key, bool isPressed)
    {
        if (State != HotkeyRecordingState.Recording)
        {
            return State;
        }

        if (isPressed)
        {
            if (key == KeyCode.VcEscape)
            {
                Cancel();
            }
            else if (_held.Add(key) && _held.Count > _keys.Count)
            {
                // A key-down of a held key is auto-repeat and changes nothing.
                _keys = [.._held];
            }
        }
        else if (_held.Remove(key) && _held.Count == 0)
        {
            State = IsValid(_keys) ? HotkeyRecordingState.Captured : HotkeyRecordingState.Rejected;
        }

        return State;
    }

    /// <summary>
    /// Cancels the recording, for example when the window loses focus. Has no effect once the recording has ended.
    /// </summary>
    public void Cancel()
    {
        if (State == HotkeyRecordingState.Recording)
        {
            State = HotkeyRecordingState.Cancelled;
            _held.Clear();
            _keys = [];
        }
    }
}
