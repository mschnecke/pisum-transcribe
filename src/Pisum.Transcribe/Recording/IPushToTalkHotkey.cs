using SharpHook.Data;

namespace Pisum.Transcribe.Recording;

/// <summary>
/// The system-wide push-to-talk hotkey. The events are raised in order on a background thread; handlers must return
/// quickly and marshal to their own context.
/// </summary>
internal interface IPushToTalkHotkey
{
    /// <summary>
    /// Raised once when all hotkey keys are held.
    /// </summary>
    event EventHandler? Pressed;

    /// <summary>
    /// Raised once when the first hotkey key is let go after <see cref="Pressed"/>.
    /// </summary>
    event EventHandler? Released;

    /// <summary>
    /// Raised instead of <see cref="Released"/> when another key is pressed while the hotkey is held, or when the hotkey
    /// keys can no longer be observed as held.
    /// </summary>
    event EventHandler? Cancelled;

    /// <summary>
    /// Raised for every key-down and key-up that the user makes while the hotkey is suspended, for recording a new
    /// hotkey. Never raised otherwise. Handlers must not keep or log the keys beyond the recording.
    /// </summary>
    event EventHandler<RawKeyEventArgs>? RawKey;

    /// <summary>
    /// Replaces the hotkey. If the previous hotkey was held after <see cref="Pressed"/>, <see cref="Cancelled"/> is
    /// raised.
    /// </summary>
    /// <param name="hotkey">The keys of the new hotkey. Not empty.</param>
    void SetHotkey(IReadOnlySet<KeyCode> hotkey);

    /// <summary>
    /// Suspends the hotkey while a new one is recorded: key events raise <see cref="RawKey"/> instead of the hotkey
    /// signals. If the hotkey was held after <see cref="Pressed"/>, <see cref="Cancelled"/> is raised first. Has no
    /// effect while already suspended.
    /// </summary>
    void Suspend();

    /// <summary>
    /// Ends the suspension, so the hotkey raises its signals again. Has no effect while not suspended.
    /// </summary>
    void Resume();
}
