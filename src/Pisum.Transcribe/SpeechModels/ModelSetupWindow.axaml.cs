using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Pisum.Transcribe.Dialogs;

namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// The setup window that downloads a speech model. Closing it during a download asks for confirmation.
/// </summary>
internal sealed partial class ModelSetupWindow : Window
{
    /// <summary>
    /// The question before closing cancels a download.
    /// </summary>
    public const string ConfirmCloseMessage = "Closing cancels the download. Close anyway?";

    /// <summary>
    /// Initializes a new instance, for the XAML loader. Use the constructor with a view model.
    /// </summary>
    public ModelSetupWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="viewModel">The view model, used by this window only.</param>
    public ModelSetupWindow(ModelSetupViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
        ConfirmDialog.AskBeforeClosing(this, ConfirmCloseMessage, viewModel.ConfirmClose);
    }

    private void OnLicenseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control {Tag: Uri uri})
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) {UseShellExecute = true})?.Dispose();
        }
    }
}
