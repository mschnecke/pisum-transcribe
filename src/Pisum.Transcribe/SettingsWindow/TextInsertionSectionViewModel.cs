using CommunityToolkit.Mvvm.ComponentModel;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// The text insertion section of the settings window: the insertion method and the clipboard restore.
/// </summary>
internal sealed partial class TextInsertionSectionViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance with the saved settings.
    /// </summary>
    /// <param name="settings">The saved text insertion settings.</param>
    public TextInsertionSectionViewModel(TextInsertionSettings settings)
    {
        Method = settings.Method;
        RestoreClipboard = settings.RestoreClipboard;
    }

    /// <summary>
    /// How the transcript is delivered.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClipboardPaste), nameof(IsTypeText), nameof(CanRestoreClipboard))]
    public partial InsertionMethod Method { get; set; }

    /// <summary>
    /// Whether "Paste via clipboard" is chosen, for a radio button.
    /// </summary>
    public bool IsClipboardPaste
    {
        get => Method == InsertionMethod.ClipboardPaste;
        set
        {
            if (value)
            {
                Method = InsertionMethod.ClipboardPaste;
            }
        }
    }

    /// <summary>
    /// Whether "Type text" is chosen, for a radio button.
    /// </summary>
    public bool IsTypeText
    {
        get => Method == InsertionMethod.TypeText;
        set
        {
            if (value)
            {
                Method = InsertionMethod.TypeText;
            }
        }
    }

    /// <summary>
    /// Whether the previous clipboard contents are put back after a paste.
    /// </summary>
    [ObservableProperty]
    public partial bool RestoreClipboard { get; set; }

    /// <summary>
    /// Whether the restore option applies, which it does only for the clipboard paste.
    /// </summary>
    public bool CanRestoreClipboard => Method == InsertionMethod.ClipboardPaste;

    /// <summary>
    /// Gets the settings as the section shows them.
    /// </summary>
    /// <returns>The text insertion settings.</returns>
    public TextInsertionSettings ToSettings()
    {
        return new TextInsertionSettings(Method, RestoreClipboard);
    }

    /// <summary>
    /// Takes newly saved values for the settings the user has not edited.
    /// </summary>
    /// <param name="previous">The saved settings the section was based on.</param>
    /// <param name="current">The newly saved settings.</param>
    public void Rebase(TextInsertionSettings previous, TextInsertionSettings current)
    {
        Method = DraftValue.Rebase(Method, previous.Method, current.Method);
        RestoreClipboard = DraftValue.Rebase(RestoreClipboard, previous.RestoreClipboard, current.RestoreClipboard);
    }
}
