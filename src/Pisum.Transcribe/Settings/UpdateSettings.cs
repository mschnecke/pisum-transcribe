namespace Pisum.Transcribe.Settings;

/// <summary>
/// The update check settings, saved as the <c>updates</c> section.
/// </summary>
/// <param name="CheckAutomatically">
/// Whether the application asks GitHub once a day whether a new version exists.
/// </param>
internal sealed record UpdateSettings(bool CheckAutomatically = true);
