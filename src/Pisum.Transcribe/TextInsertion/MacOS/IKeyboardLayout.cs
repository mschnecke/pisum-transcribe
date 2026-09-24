namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// The current keyboard layout, for the key of Command+V. Call it on the UI thread: macOS asserts that its input source
/// functions run on the main thread.
/// </summary>
internal interface IKeyboardLayout
{
    /// <summary>
    /// Finds the key that produces "v" with Command held in the current layout.
    /// </summary>
    /// <returns>The macOS key code, or <see langword="null"/> when no key of the layout does.</returns>
    ushort? FindPasteKeyCode();

    /// <summary>
    /// Calls <paramref name="changed"/> after the user selects another input source.
    /// </summary>
    /// <param name="changed">Called on the thread that macOS delivers the notification on.</param>
    /// <returns>Stops the notifications when disposed, on the UI thread.</returns>
    IDisposable ObserveChanges(Action changed);
}
