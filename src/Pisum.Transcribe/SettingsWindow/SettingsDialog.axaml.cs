using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Pisum.Transcribe.Dialogs;
using Pisum.Transcribe.Hosting;
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
internal sealed partial class SettingsDialog : Window
{
    /// <summary>
    /// The question before closing cancels a download.
    /// </summary>
    public const string ConfirmCloseMessage = "Closing cancels the download. Close anyway?";

    private readonly SettingsViewModel? _viewModel;

    /// <summary>
    /// Initializes a new instance, for the XAML loader. Use the constructor with a view model.
    /// </summary>
    public SettingsDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="viewModel">The view model, used by this window only.</param>
    public SettingsDialog(SettingsViewModel viewModel)
        : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        // Picks up models installed by the setup window or deleted as damaged by the engine.
        Activated += (_, _) => viewModel.Model.RefreshInstalled();
        Deactivated += (_, _) => viewModel.Dictation.CancelHotkeyRecording();
        Closed += (_, _) => viewModel.OnClosed();
        AddHandler(KeyDownEvent, OnPreviewKey, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKey, RoutingStrategies.Tunnel);
        ConfirmDialog.AskBeforeClosing(this, ConfirmCloseMessage, viewModel.ConfirmClose);
    }

    /// <summary>
    /// Asks the user whether to delete a model. The UI thread keeps running while the dialog shows.
    /// </summary>
    /// <param name="model">The model to delete.</param>
    /// <returns><see langword="true"/> if the user confirmed.</returns>
    public bool ConfirmDelete(SpeechModel model)
    {
        // The view model asks synchronously, as WPF's message box did, so the dialog is awaited in a nested frame.
        var answer = ConfirmDialog.ShowAsync(this, $"Delete {model.DisplayName}? You can download it again later.");
        DispatcherWait.Until(answer);
        return answer.Result;
    }

    private void OnPreviewKey(object? sender, KeyEventArgs e)
    {
        e.Handled |= _viewModel?.Dictation.IsRecordingHotkey == true;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnLicenseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control {Tag: Uri uri})
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) {UseShellExecute = true})?.Dispose();
        }
    }
}
