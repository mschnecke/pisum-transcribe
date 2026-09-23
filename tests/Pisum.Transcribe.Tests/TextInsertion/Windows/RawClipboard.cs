using System.Diagnostics;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Ole;

namespace Pisum.Transcribe.Tests.TextInsertion;

/// <summary>
/// Reads, writes and holds the Windows clipboard with plain Win32 calls, the way other applications see it.
/// </summary>
internal static class RawClipboard
{
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Reads the bytes of a registered clipboard format.
    /// </summary>
    /// <returns>The bytes, or <see langword="null"/> if the clipboard does not hold the format.</returns>
    public static byte[]? Read(string format)
    {
        return Read(PInvoke.RegisterClipboardFormat(format));
    }

    /// <summary>
    /// Reads the bytes of a clipboard format.
    /// </summary>
    /// <returns>The bytes, or <see langword="null"/> if the clipboard does not hold the format.</returns>
    public static unsafe byte[]? Read(uint format)
    {
        Open();
        try
        {
            var handle = PInvoke.GetClipboardData(format);
            if (handle.IsNull)
            {
                return null;
            }

            var memory = (HGLOBAL) (nint) handle;
            var bytes = new byte[(int) PInvoke.GlobalSize(memory)];
            var pointer = PInvoke.GlobalLock(memory);
            try
            {
                new ReadOnlySpan<byte>(pointer, bytes.Length).CopyTo(bytes);
            }
            finally
            {
                PInvoke.GlobalUnlock(memory);
            }

            return bytes;
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    /// <summary>
    /// Reads the Unicode text on the clipboard, without its terminating null.
    /// </summary>
    public static string GetText()
    {
        var bytes = Read((uint) CLIPBOARD_FORMAT.CF_UNICODETEXT);
        if (bytes is null)
        {
            return string.Empty;
        }

        var text = Encoding.Unicode.GetString(bytes);
        var end = text.IndexOf('\0');
        return end < 0 ? text : text[..end];
    }

    /// <summary>
    /// Places text on the clipboard as Unicode text.
    /// </summary>
    public static void SetText(string text)
    {
        Set([((uint) CLIPBOARD_FORMAT.CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + '\0'))]);
    }

    /// <summary>
    /// Replaces the clipboard contents with the given formats and their bytes, in their order.
    /// </summary>
    public static unsafe void Set(IReadOnlyList<(uint Format, byte[] Bytes)> formats)
    {
        // SetClipboardData fails after EmptyClipboard when the clipboard was opened without an owner window. The
        // window goes again right away, so nothing has to pump messages for it.
        var owner = PInvoke.CreateWindowEx(default, "STATIC", null, default, 0, 0, 0, 0, HWND.HWND_MESSAGE);
        owner.IsNull.ShouldBeFalse("The test could not create a clipboard owner window.");
        try
        {
            Open(owner);
            try
            {
                ((bool) PInvoke.EmptyClipboard()).ShouldBeTrue("The test could not empty the clipboard.");
                foreach (var (format, bytes) in formats)
                {
                    Put(format, bytes);
                }
            }
            finally
            {
                PInvoke.CloseClipboard();
            }
        }
        finally
        {
            PInvoke.DestroyWindow(owner);
        }
    }

    /// <summary>
    /// Keeps the clipboard open on a background thread until disposed, like a busy application.
    /// </summary>
    public static IDisposable Hold()
    {
        var opened = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            Open();
            opened.Set();
            release.Wait();
            PInvoke.CloseClipboard();
        }) {IsBackground = true};
        thread.Start();
        opened.Wait(OpenTimeout * 2).ShouldBeTrue("The test could not open the clipboard.");
        return new Holder(release, thread);
    }

    private static unsafe void Put(uint format, byte[] bytes)
    {
        var memory = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, (nuint) bytes.Length);
        memory.IsNull.ShouldBeFalse("The test could not allocate clipboard memory.");
        bytes.CopyTo(new Span<byte>(PInvoke.GlobalLock(memory), bytes.Length));
        PInvoke.GlobalUnlock(memory);

        // The system owns the memory once the call succeeds.
        PInvoke.SetClipboardData(format, (HANDLE) (nint) memory).IsNull
            .ShouldBeFalse("The test could not put a format on the clipboard.");
    }

    private static void Open(HWND owner = default)
    {
        // Clipboard monitors, such as the clipboard history service, open the clipboard briefly after every change.
        var stopwatch = Stopwatch.StartNew();
        while (!PInvoke.OpenClipboard(owner))
        {
            stopwatch.Elapsed.ShouldBeLessThan(OpenTimeout, "The test could not open the clipboard.");
            Thread.Sleep(10);
        }
    }

    private sealed class Holder(ManualResetEventSlim release, Thread thread) : IDisposable
    {
        public void Dispose()
        {
            release.Set();
            thread.Join();
        }
    }
}
