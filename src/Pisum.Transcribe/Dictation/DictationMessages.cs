namespace Pisum.Transcribe.Dictation;

/// <summary>
/// The user-facing strings of the dictation flow: overlay texts, tray tooltips and notifications.
/// </summary>
internal static class DictationMessages
{
    /// <summary>
    /// The product name, which every tray tooltip contains.
    /// </summary>
    public const string ProductName = "Pisum Transcribe";

    /// <summary>
    /// The overlay text while recording, followed by the elapsed time.
    /// </summary>
    public const string OverlayRecording = "Recording";

    /// <summary>
    /// The overlay text while the audio is transcribed.
    /// </summary>
    public const string OverlayTranscribing = "Transcribing…";

    /// <summary>
    /// The overlay text when the hotkey is pressed while a dictation is still processing.
    /// </summary>
    public const string OverlayBusy = "Still processing…";

    /// <summary>
    /// The overlay text when the transcript is empty.
    /// </summary>
    public const string OverlayNoSpeech = "No speech detected";

    /// <summary>
    /// The tray menu item that cancels the dictation being transcribed.
    /// </summary>
    public const string CancelTranscriptionMenuItem = "Cancel transcription";

    /// <summary>
    /// The tray state while recording.
    /// </summary>
    public const string RecordingState = "Recording…";

    /// <summary>
    /// The tray state while transcribing.
    /// </summary>
    public const string TranscribingState = "Transcribing…";

    /// <summary>
    /// The tray state while no model is loaded.
    /// </summary>
    public const string NoModelState = "No model installed";

    /// <summary>
    /// The tray state while the model loads.
    /// </summary>
    public const string LoadingState = "Loading model…";

    /// <summary>
    /// The tray state after the model failed.
    /// </summary>
    public const string FailedState = "Model failed to load";

    /// <summary>
    /// The notification title when the hotkey is pressed while the engine is not ready.
    /// </summary>
    public const string NotReadyTitle = "Dictation not available";

    /// <summary>
    /// The reason when the hotkey is pressed while no model is loaded. Downloads run only while the setup window is open,
    /// which <b>Download model…</b> brings to the front, so this also covers a download in progress.
    /// </summary>
    public const string NoModelMessage =
        "No speech model is installed yet. Choose Download model… in the tray menu to download one or see the " +
        "download progress.";

    /// <summary>
    /// The reason when the hotkey is pressed while the model loads.
    /// </summary>
    public const string LoadingMessage = "The speech model is still loading. Try again in a moment.";

    /// <summary>
    /// The reason when the hotkey is pressed after the model failed and the engine gives no failure message.
    /// </summary>
    public const string FailedMessage = "The speech model failed to load. Details are in the log.";

    /// <summary>
    /// The notification title when the transcript was left on the clipboard.
    /// </summary>
    public const string CopiedTitle = "Text copied to the clipboard";

    /// <summary>
    /// The reason when the target window was no longer in the foreground.
    /// </summary>
    public const string TargetWindowChangedReason = "the active window changed";

    /// <summary>
    /// The reason when the target window belongs to an elevated process.
    /// </summary>
    public const string TargetWindowElevatedReason = "the target window runs as administrator";

    /// <summary>
    /// The reason when modifier keys were still held.
    /// </summary>
    public const string ModifierKeysHeldReason = "modifier keys were held";

    /// <summary>
    /// The notification title when the transcript was neither inserted nor copied.
    /// </summary>
    public const string NotDeliveredTitle = "Text not inserted";

    /// <summary>
    /// The notification text when the transcript was neither inserted nor copied.
    /// </summary>
    public const string ClipboardUnavailableMessage =
        "The text could not be inserted or copied, because another application is using the clipboard.";

    /// <summary>
    /// The notification title when the recording reached the model's maximum input duration.
    /// </summary>
    public const string MaxDurationTitle = "Maximum dictation length reached";

    /// <summary>
    /// The notification text when the recording reached the model's maximum input duration.
    /// </summary>
    public const string MaxDurationMessage =
        "The recording stopped at the maximum dictation length. What was recorded is transcribed and inserted.";

    /// <summary>
    /// The notification title when the recording could not start or was lost. The text is the error's message.
    /// </summary>
    public const string RecordingFailedTitle = "Recording failed";

    /// <summary>
    /// The notification title when the transcription or insertion failed.
    /// </summary>
    public const string DictationFailedTitle = "Dictation failed";

    /// <summary>
    /// The notification text when the dictation failed for an unexpected reason.
    /// </summary>
    public const string DictationFailedMessage = "The dictation failed. Details are in the log.";

    /// <summary>
    /// Builds the tray state when the engine is ready.
    /// </summary>
    /// <param name="backend">The active backend, such as <c>Vulkan</c> or <c>CPU</c>.</param>
    /// <returns>The state, such as "Ready (CPU)".</returns>
    public static string ReadyState(string? backend)
    {
        return $"Ready ({backend})";
    }

    /// <summary>
    /// Builds a tray tooltip, which always contains the product name.
    /// </summary>
    /// <param name="state">The state, such as <see cref="RecordingState"/>.</param>
    /// <returns>The tooltip, such as "Pisum Transcribe – Recording…".</returns>
    public static string ToolTip(string state)
    {
        return $"{ProductName} – {state}";
    }

    /// <summary>
    /// Builds the notification text when the transcript was left on the clipboard.
    /// </summary>
    /// <param name="reason">Why it was not inserted, such as <see cref="TargetWindowChangedReason"/>.</param>
    /// <returns>The notification text.</returns>
    public static string CopiedMessage(string reason)
    {
        return $"The text was not inserted, because {reason}. Paste it with Ctrl+V.";
    }
}
