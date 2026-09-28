using System.Globalization;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.Transcription;

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
    /// The notification title when the tray menu could not change the task.
    /// </summary>
    public const string TaskSwitchFailedTitle = "Mode not changed";

    /// <summary>
    /// The notification text when the tray menu could not change the task.
    /// </summary>
    public const string TaskSwitchFailedMessage = "The settings could not be saved. Details are in the log.";

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
    /// The tray state on macOS while the Accessibility grant isn't in effect, so the hotkey doesn't work.
    /// </summary>
    public const string AccessibilityNotInEffectState = "Accessibility access needed for the hotkey";

    /// <summary>
    /// The tray state on macOS while Secure Event Input is on, so the hotkey sees no keys.
    /// </summary>
    public const string SecureInputOnState = "Paused while secure input is on";

    /// <summary>
    /// The platform's paste shortcut, as the fallback notification names it.
    /// </summary>
#if WINDOWS
    public const string PasteShortcut = "Ctrl+V";
#else
    public const string PasteShortcut = "Command+V";
#endif

    /// <summary>
    /// The notification title when the hotkey is pressed while the engine is not ready.
    /// </summary>
    public const string NotReadyTitle = "Dictation not available";

    /// <summary>
    /// The reason when the hotkey is pressed while no model is loaded. Downloads run only while the setup window is open,
    /// which <b>Download model…</b> brings to the front, so this also covers a download in progress. On macOS the item
    /// is <b>Set up Pisum Transcribe…</b> in the menu bar, as in the app bundle; a run without a bundle shows
    /// <b>Download model…</b> instead.
    /// </summary>
#if WINDOWS
    public const string NoModelMessage =
        "No speech model is installed yet. Choose Download model… in the tray menu to download one or see the " +
        "download progress.";
#else
    public const string NoModelMessage =
        "No speech model is installed yet. Choose Set up Pisum Transcribe… in the menu bar to download one or see " +
        "the download progress.";
#endif

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
    /// The reason on macOS when Secure Event Input was on, for example because a password field has focus.
    /// </summary>
    public const string SecureInputReason = "secure input is on, for example in a password field";

    /// <summary>
    /// The reason on macOS when the Accessibility grant isn't in effect, so keystrokes would not arrive.
    /// </summary>
    public const string KeystrokesNotAllowedReason = "Accessibility access isn't in effect";

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
    /// Names a language as the settings window's language pickers do.
    /// </summary>
    /// <param name="code">The ISO 639-1 code, such as <c>de</c>.</param>
    /// <returns>The English name, such as "German", or <paramref name="code"/> for an unknown language.</returns>
    public static string LanguageName(string code)
    {
        try
        {
            return CultureInfo.GetCultureInfo(code).EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }

    /// <summary>
    /// Builds the tray menu item that switches to <see cref="TranscriptionTask.Transcribe"/>.
    /// </summary>
    /// <param name="sourceLanguage">The source language as an ISO 639-1 code.</param>
    /// <returns>The text, such as "Transcribe (German)".</returns>
    public static string TranscribeMenuItem(string sourceLanguage)
    {
        return $"Transcribe ({LanguageName(sourceLanguage)})";
    }

    /// <summary>
    /// Builds the tray menu item that switches to <see cref="TranscriptionTask.Translate"/>.
    /// </summary>
    /// <param name="sourceLanguage">The source language as an ISO 639-1 code.</param>
    /// <param name="targetLanguage">The target language as an ISO 639-1 code.</param>
    /// <returns>The text, such as "Translate (German → English)".</returns>
    public static string TranslateMenuItem(string sourceLanguage, string targetLanguage)
    {
        return $"Translate ({LanguageName(sourceLanguage)} → {LanguageName(targetLanguage)})";
    }

    /// <summary>
    /// Builds the tray tooltip's line that names the task and its languages.
    /// </summary>
    /// <param name="settings">The transcription settings.</param>
    /// <returns>The line, such as "Translate: German → English" or "Transcribe: German".</returns>
    public static string ModeLine(TranscriptionSettings settings)
    {
        return settings.Task == TranscriptionTask.Transcribe
            ? $"Transcribe: {LanguageName(settings.SourceLanguage)}"
            : $"Translate: {LanguageName(settings.SourceLanguage)} → {LanguageName(settings.TargetLanguage)}";
    }

    /// <summary>
    /// Builds a tray tooltip, which always contains the product name.
    /// </summary>
    /// <param name="state">The state, such as <see cref="RecordingState"/>.</param>
    /// <param name="modeLine">The second line, from <see cref="ModeLine"/>.</param>
    /// <returns>The tooltip, such as "Pisum Transcribe – Recording…" and "Translate: German → English".</returns>
    public static string ToolTip(string state, string modeLine)
    {
        return $"{ProductName} – {state}\n{modeLine}";
    }

    /// <summary>
    /// Builds the notification text when the transcript was left on the clipboard.
    /// </summary>
    /// <param name="reason">Why it was not inserted, such as <see cref="TargetWindowChangedReason"/>.</param>
    /// <returns>The notification text.</returns>
    public static string CopiedMessage(string reason)
    {
        return $"The text was not inserted, because {reason}. Paste it with {PasteShortcut}.";
    }
}
