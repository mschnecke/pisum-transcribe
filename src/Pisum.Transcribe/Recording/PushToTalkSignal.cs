namespace Pisum.Transcribe.Recording;

/// <summary>
/// A change of the push-to-talk hotkey state.
/// </summary>
internal enum PushToTalkSignal
{
    /// <summary>
    /// All hotkey keys are held.
    /// </summary>
    Pressed,

    /// <summary>
    /// The first hotkey key was let go after <see cref="Pressed"/>.
    /// </summary>
    Released,

    /// <summary>
    /// Another key was pressed after <see cref="Pressed"/>, or the state was reset. No <see cref="Released"/> follows.
    /// </summary>
    Cancelled,
}
