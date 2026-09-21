using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Dictation;

/// <summary>
/// Shows the user what a dictation is doing: the recording overlay, the tray icon and notifications. Call the members
/// from any thread; they return without waiting for the UI.
/// </summary>
internal interface IDictationFeedback
{
    /// <summary>
    /// Raised on the UI thread when the user chooses <b>Cancel transcription</b> in the tray menu.
    /// </summary>
    event EventHandler? CancelRequested;

    /// <summary>
    /// Shows the overlay in its starting look on the target window's monitor, while the microphone opens. The tray
    /// stays idle.
    /// </summary>
    /// <param name="target">The window the transcript will be inserted into.</param>
    void ShowStarting(InsertionTarget target);

    /// <summary>
    /// Shows "Recording" with the elapsed time from 0:00, and the recording tray state.
    /// </summary>
    void ShowRecording();

    /// <summary>
    /// Shows "Transcribing…" and the transcribing tray state.
    /// </summary>
    void ShowTranscribing();

    /// <summary>
    /// Briefly shows "Still processing…" in the overlay.
    /// </summary>
    void ShowBusy();

    /// <summary>
    /// Briefly shows "No speech detected" in the overlay.
    /// </summary>
    void ShowNoSpeech();

    /// <summary>
    /// Ends the dictation: hides the overlay once a brief message has been shown, and returns the tray to the engine
    /// status.
    /// </summary>
    void ShowIdle();

    /// <summary>
    /// Shows a tray notification.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification text.</param>
    void Notify(string title, string message);
}
