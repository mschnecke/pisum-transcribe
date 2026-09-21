using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Navigation;

namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// The setup window that downloads a speech model. Closing it during a download asks for confirmation.
/// </summary>
internal sealed partial class ModelSetupWindow
{
    private readonly ModelSetupViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="viewModel">The view model, used by this window only.</param>
    public ModelSetupWindow(ModelSetupViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
    }

    /// <inheritdoc />
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        e.Cancel = !_viewModel.ConfirmClose(ConfirmCancelDownload);
    }

    private bool ConfirmCancelDownload()
    {
        return MessageBox.Show(this, "Closing cancels the download. Close anyway?", Title, MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) {UseShellExecute = true})?.Dispose();
        e.Handled = true;
    }
}
