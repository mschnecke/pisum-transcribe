using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Navigation;
using Pisum.Transcribe.SpeechModels;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// The settings window. <b>Save</b> applies the edits and keeps it open; closing it discards unsaved edits, and asks
/// first only while a download started here is running.
/// </summary>
/// <remarks>
/// While a new hotkey is recorded, the window swallows all keys, so that Esc cancels only the recording and Enter or
/// Space press no button. Losing focus ends the recording, so keys typed in other applications are never recorded.
/// </remarks>
internal sealed partial class SettingsDialog
{
    private readonly SettingsViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="viewModel">The view model, used by this window only.</param>
    public SettingsDialog(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>
    /// Asks the user whether to delete a model.
    /// </summary>
    /// <param name="model">The model to delete.</param>
    /// <returns><see langword="true"/> if the user confirmed.</returns>
    public bool ConfirmDelete(SpeechModel model)
    {
        return MessageBox.Show(this, $"Delete {model.DisplayName}? You can download it again later.", Title,
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    /// <inheritdoc />
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);

        // Picks up models installed by the setup window or deleted as damaged by the engine.
        _viewModel.Model.RefreshInstalled();
    }

    /// <inheritdoc />
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        _viewModel.Dictation.CancelHotkeyRecording();
    }

    /// <inheritdoc />
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        e.Handled |= _viewModel.Dictation.IsRecordingHotkey;
    }

    /// <inheritdoc />
    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        e.Handled |= _viewModel.Dictation.IsRecordingHotkey;
    }

    /// <inheritdoc />
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        e.Cancel = !_viewModel.ConfirmClose(ConfirmCancelDownload);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _viewModel.OnClosed();
        base.OnClosed(e);
    }

    private bool ConfirmCancelDownload()
    {
        return MessageBox.Show(this, "Closing cancels the download. Close anyway?", Title, MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) {UseShellExecute = true})?.Dispose();
        e.Handled = true;
    }
}
