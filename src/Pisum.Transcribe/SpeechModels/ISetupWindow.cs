namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// The one setup window of the process, as other features see it, such as the relaunch that shows its notice there.
/// </summary>
internal interface ISetupWindow
{
    /// <summary>
    /// Whether the setup window is open. Read it on the UI thread.
    /// </summary>
    bool IsOpen { get; }
}
