namespace Pisum.Transcribe.Hosting;

/// <summary>
/// Marks work the user waits for, so macOS doesn't throttle the application through App Nap while it runs (design D6
/// of add-macos-dictation). Nothing happens on Windows.
/// </summary>
internal interface IProcessActivity
{
    /// <summary>
    /// Begins an activity. Activities may nest and may be begun and ended on any thread.
    /// </summary>
    /// <param name="reason">What the application does, as macOS lists it, such as "Dictation".</param>
    /// <returns>Ends the activity when disposed.</returns>
    IDisposable Begin(string reason);
}
