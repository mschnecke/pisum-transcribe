using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace Pisum.Transcribe.Tests.TextInsertion;

/// <summary>
/// Reads and holds the Windows clipboard with plain Win32 calls, the way other applications see it.
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
        var formatId = RegisterClipboardFormatW(format);
        Open();
        try
        {
            var handle = GetClipboardData(formatId);
            if (handle == 0)
            {
                return null;
            }

            var bytes = new byte[(int) GlobalSize(handle)];
            var pointer = GlobalLock(handle);
            try
            {
                Marshal.Copy(pointer, bytes, 0, bytes.Length);
            }
            finally
            {
                GlobalUnlock(handle);
            }

            return bytes;
        }
        finally
        {
            CloseClipboard();
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
            CloseClipboard();
        }) {IsBackground = true};
        thread.Start();
        opened.Wait(OpenTimeout * 2).ShouldBeTrue("The test could not open the clipboard.");
        return new Holder(release, thread);
    }

    /// <summary>
    /// Runs a WPF call on a new STA thread, as <see cref="Clipboard"/> requires.
    /// </summary>
    public static T OnSta<T>(Func<T> action)
    {
        var result = default(T);
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw new InvalidOperationException("The clipboard call failed.", error);
        }

        return result!;
    }

    /// <summary>
    /// Places data on the clipboard through WPF.
    /// </summary>
    public static void Set(DataObject data)
    {
        OnSta(() =>
        {
            Clipboard.SetDataObject(data, true);
            return true;
        });
    }

    /// <summary>
    /// Reads the Unicode text on the clipboard through WPF.
    /// </summary>
    public static string GetText()
    {
        return OnSta(Clipboard.GetText);
    }

    private static void Open()
    {
        // Clipboard monitors, such as the clipboard history service, open the clipboard briefly after every change.
        var stopwatch = Stopwatch.StartNew();
        while (!OpenClipboard(0))
        {
            stopwatch.Elapsed.ShouldBeLessThan(OpenTimeout, "The test could not open the clipboard.");
            Thread.Sleep(10);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint RegisterClipboardFormatW(string format);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint newOwner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern nint GetClipboardData(uint format);

    [DllImport("kernel32.dll")]
    private static extern nuint GlobalSize(nint memory);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint memory);

    private sealed class Holder(ManualResetEventSlim release, Thread thread) : IDisposable
    {
        public void Dispose()
        {
            release.Set();
            thread.Join();
        }
    }
}
