using CommunityToolkit.Mvvm.ComponentModel;
using Pisum.Transcribe.Settings;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// The general section of the settings window: starting at sign-in, as "Start with Windows" on Windows and "Open at
/// login" on macOS, and the update check.
/// </summary>
internal sealed partial class GeneralSectionViewModel : ObservableObject
{
#if WINDOWS
    /// <summary>
    /// The label of the option that starts the application at sign-in.
    /// </summary>
    public const string StartAtSignInLabel = "Start with _Windows";

    /// <summary>
    /// The hint below the option.
    /// </summary>
    public const string StartAtSignInHint =
        "Starts Pisum Transcribe when you sign in to Windows. Applies to your user account only.";
#else
    /// <summary>
    /// The label of the option that starts the application at sign-in.
    /// </summary>
    public const string StartAtSignInLabel = "Open at _login";

    /// <summary>
    /// The hint below the option.
    /// </summary>
    public const string StartAtSignInHint =
        "Opens Pisum Transcribe when you log in to your Mac. Applies to your user account only.";
#endif

    /// <summary>
    /// The hint when the platform needs the user's approval, which only macOS asks for.
    /// </summary>
    public const string ApprovalHint = "Allow Pisum Transcribe in System Settings → General → Login Items.";

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="isStartAtSignInAvailable">Whether the platform offers starting at sign-in.</param>
    /// <param name="startAtSignIn">Whether the platform starts the application at sign-in now.</param>
    /// <param name="requiresApproval">Whether the platform needs the user's approval first.</param>
    /// <param name="updates">The saved update check settings.</param>
    public GeneralSectionViewModel(bool isStartAtSignInAvailable,
                                   bool startAtSignIn,
                                   bool requiresApproval,
                                   UpdateSettings updates)
    {
        IsStartAtSignInAvailable = isStartAtSignInAvailable;
        StartAtSignIn = startAtSignIn;
        RequiresApproval = requiresApproval;
        CheckForUpdates = updates.CheckAutomatically;
    }

    /// <summary>
    /// Whether the section shows the option that starts the application at sign-in.
    /// </summary>
    public bool IsStartAtSignInAvailable { get; }

    /// <summary>
    /// Whether the platform starts the application when the user signs in.
    /// </summary>
    [ObservableProperty]
    public partial bool StartAtSignIn { get; set; }

    /// <summary>
    /// Whether the platform needs the user's approval before it starts the application at sign-in, which shows
    /// <see cref="ApprovalHint"/>.
    /// </summary>
    [ObservableProperty]
    public partial bool RequiresApproval { get; set; }

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
