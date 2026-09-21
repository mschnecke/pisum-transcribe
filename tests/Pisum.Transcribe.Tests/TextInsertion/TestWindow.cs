using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Pisum.Transcribe.Tests.TextInsertion;

/// <summary>
/// A WPF window with a focused multi-line <see cref="TextBox"/>, on its own STA thread in the test process.
/// </summary>
internal sealed class TestWindow : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Window _window;
    private readonly TextBox _textBox;

    private TestWindow(Dispatcher dispatcher, Window window, TextBox textBox, nint handle)
    {
        _dispatcher = dispatcher;
        _window = window;
        _textBox = textBox;
        Handle = handle;
    }

    /// <summary>
    /// The window handle.
    /// </summary>
    public nint Handle { get; }

    /// <summary>
    /// The text box contents.
    /// </summary>
    public string Text => _dispatcher.Invoke(() => _textBox.Text);

    /// <summary>
    /// Opens the window and tries to bring it to the foreground. Windows may refuse that for a test process.
    /// </summary>
    public static TestWindow Open()
    {
        var opened = new TaskCompletionSource<TestWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var textBox = new TextBox {AcceptsReturn = true};
            var window = new Window
            {
                Title = "Pisum Transcribe text insertion test",
                Width = 400,
                Height = 200,
                Topmost = true,
                Content = textBox,
            };
            window.Show();
            window.Activate();
            textBox.Focus();
            opened.SetResult(new TestWindow(Dispatcher.CurrentDispatcher, window, textBox,
                new WindowInteropHelper(window).Handle));
            Dispatcher.Run();
        }) {IsBackground = true, Name = "Test window"};
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return opened.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Clears the text box.
    /// </summary>
    public void Clear()
    {
        _dispatcher.Invoke(() => _textBox.Clear());
    }

    /// <summary>
    /// Waits until the text box holds the expected text, or returns its last contents after the timeout.
    /// </summary>
    public async Task<string> WaitForTextAsync(string expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Text != expected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        return Text;
    }

    public void Dispose()
    {
        _dispatcher.Invoke(() => _window.Close());
        _dispatcher.InvokeShutdown();
    }
}
