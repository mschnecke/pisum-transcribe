using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Pisum.Transcribe.Dialogs;

/// <summary>
/// A modal question with <b>Yes</b> and <b>No</b>, where <b>No</b> is the default. Closing it any other way, Esc
/// included, answers <b>No</b>. It closes with its owner.
/// </summary>
internal sealed partial class ConfirmDialog : Window
{
    /// <summary>
    /// Initializes a new instance. Use <see cref="ShowAsync"/>.
    /// </summary>
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Asks a question in a dialog owned by <paramref name="owner"/>, with the owner's title.
    /// </summary>
    /// <param name="owner">The window that asks. It is disabled while the dialog shows.</param>
    /// <param name="message">The question.</param>
    /// <returns>A task that completes with <see langword="true"/> if the user chose <b>Yes</b>.</returns>
    public static Task<bool> ShowAsync(Window owner, string message)
    {
        var dialog = new ConfirmDialog {Title = owner.Title};
        dialog.Message.Text = message;
        return dialog.ShowDialog<bool>(owner);
    }

    /// <summary>
    /// Asks before <paramref name="window"/> closes, whenever <paramref name="confirmClose"/> wants to ask. The dialog
    /// is asynchronous, so closing is cancelled first, and the window closes again after <b>Yes</b>.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="message">The question.</param>
    /// <param name="confirmClose">
    /// Decides whether the window may close, as the view models' <c>ConfirmClose</c> does: it gets a function that
    /// asks the user, and returns <see langword="true"/> if the window may close.
    /// </param>
    public static void AskBeforeClosing(Window window, string message, Func<Func<bool>, bool> confirmClose)
    {
        var confirmed = false;
        var asking = false;
        var closed = false;

        window.Closing += (_, e) =>
        {
            var ask = false;
            e.Cancel = !confirmClose(() =>
            {
                // The answer is known only on the second close, after Yes.
                ask = !confirmed;
                return confirmed;
            });
            if (ask && !asking)
            {
                _ = AskAndCloseAsync();
            }
        };
        window.Closed += (_, _) => closed = true;

        async Task AskAndCloseAsync()
        {
            asking = true;
            try
            {
                confirmed = await ShowAsync(window, message);
            }
            finally
            {
                asking = false;
            }

            // The window may have closed while it asked: the download finished, or the application exits.
            if (confirmed && !closed)
            {
                window.Close();
            }
        }
    }

    private void OnYesClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnNoClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
