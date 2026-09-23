using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Pisum.Transcribe.Tests.TextInsertion;

/// <summary>
/// A top-level multi-line <c>EDIT</c> window on its own thread in the test process, focused and on top.
/// </summary>
/// <remarks>
/// The window is its own edit control, so the focus needs no forwarding. Its message loop translates key messages,
/// which turns Ctrl+V into the control's paste and SharpHook's <c>VK_PACKET</c> input into characters.
/// </remarks>
internal sealed class TestWindow : IDisposable
{
    private readonly uint _threadId;
    private readonly HWND _window;

    private TestWindow(uint threadId, HWND window)
    {
        _threadId = threadId;
        _window = window;
    }

    /// <summary>
    /// The window handle.
    /// </summary>
    public nint Handle => _window;

    /// <summary>
    /// The window contents. A multi-line edit control stores a line break as CR LF.
    /// </summary>
    public string Text
    {
        get
        {
            var buffer = new char[PInvoke.GetWindowTextLength(_window) + 1];
            var length = PInvoke.GetWindowText(_window, buffer);
            return new string(buffer, 0, length);
        }
    }

    /// <summary>
    /// Opens the window and tries to bring it to the foreground. Windows may refuse that for a test process.
    /// </summary>
    public static TestWindow Open()
    {
        var opened = new TaskCompletionSource<TestWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => Run(opened)) {IsBackground = true, Name = "Test window"};
        thread.Start();
        return opened.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Clears the window contents.
    /// </summary>
    public void Clear()
    {
        PInvoke.SetWindowText(_window, string.Empty);
    }

    /// <summary>
    /// Waits until the window holds the expected text, or returns its last contents after the timeout.
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
        PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
    }

    private static unsafe void Run(TaskCompletionSource<TestWindow> opened)
    {
        // No window name: for an edit control that is its contents, not a title, and the window starts empty.
        var window = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_TOPMOST, "EDIT", null,
            WINDOW_STYLE.WS_OVERLAPPEDWINDOW | WINDOW_STYLE.WS_VISIBLE | (WINDOW_STYLE) PInvoke.ES_MULTILINE,
            100, 100, 400, 200, default);
        if (window.IsNull)
        {
            opened.SetException(new InvalidOperationException("The test window could not be created."));
            return;
        }

        PInvoke.SetForegroundWindow(window);
        PInvoke.SetFocus(window);
        opened.SetResult(new TestWindow(PInvoke.GetCurrentThreadId(), window));

        while (PInvoke.GetMessage(out var message, default, 0, 0))
        {
            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }

        PInvoke.DestroyWindow(window);
    }
}
