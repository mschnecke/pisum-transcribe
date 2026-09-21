namespace Pisum.Transcribe.Dictation;

/// <summary>
/// The recording overlay: a small pill that never takes focus. Call all members on the UI thread.
/// </summary>
internal interface IRecordingOverlay
{
    /// <summary>
    /// Places the overlay at the bottom center of the work area of the target window's monitor and shows the starting
    /// look: a grey dot and no text.
    /// </summary>
    /// <param name="targetWindow">The target window handle, or 0 for the primary monitor.</param>
    void ShowStarting(nint targetWindow);

    /// <summary>
    /// Shows a red dot and "Recording" with the elapsed time from 0:00.
    /// </summary>
    void ShowRecording();

    /// <summary>
    /// Shows a spinner and "Transcribing…".
    /// </summary>
    void ShowTranscribing();

    /// <summary>
    /// Shows a short text, such as "No speech detected", where it is.
    /// </summary>
    /// <param name="text">The text.</param>
    void ShowMessage(string text);

    /// <summary>
    /// Hides the overlay.
    /// </summary>
    void Hide();
}
