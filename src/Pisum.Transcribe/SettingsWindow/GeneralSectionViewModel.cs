using CommunityToolkit.Mvvm.ComponentModel;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// The general section of the settings window: starting with Windows.
/// </summary>
internal sealed partial class GeneralSectionViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="startWithWindows">Whether Windows starts the application at sign-in now.</param>
    public GeneralSectionViewModel(bool startWithWindows)
    {
        StartWithWindows = startWithWindows;
    }

    /// <summary>
    /// Whether Windows starts the application when the user signs in.
    /// </summary>
    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }
}
