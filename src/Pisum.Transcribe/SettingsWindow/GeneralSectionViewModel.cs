using CommunityToolkit.Mvvm.ComponentModel;
using Pisum.Transcribe.Settings;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// The general section of the settings window: starting with Windows and the update check.
/// </summary>
internal sealed partial class GeneralSectionViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="startWithWindows">Whether Windows starts the application at sign-in now.</param>
    /// <param name="updates">The saved update check settings.</param>
    public GeneralSectionViewModel(bool startWithWindows, UpdateSettings updates)
    {
        StartWithWindows = startWithWindows;
        CheckForUpdates = updates.CheckAutomatically;
    }

    /// <summary>
    /// Whether Windows starts the application when the user signs in.
    /// </summary>
    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    /// <summary>
    /// Whether the application asks GitHub once a day whether a new version exists.
    /// </summary>
    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; }

    /// <summary>
    /// Takes the newly saved update check setting, unless the user has edited it.
    /// </summary>
    /// <param name="previous">The saved settings the section was based on.</param>
    /// <param name="current">The newly saved settings.</param>
    public void Rebase(UpdateSettings previous, UpdateSettings current)
    {
        CheckForUpdates = DraftValue.Rebase(CheckForUpdates, previous.CheckAutomatically, current.CheckAutomatically);
    }
}
