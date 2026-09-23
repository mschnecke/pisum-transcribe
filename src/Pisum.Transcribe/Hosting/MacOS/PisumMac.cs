using System.Runtime.InteropServices;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// The functions of the Swift helper <c>libPisumMac.dylib</c> (design D3 of add-macos-shell), and the libc functions
/// that go with them. Call the helper's functions only while <see cref="MacNativeLibrary.IsAvailable"/> is
/// <see langword="true"/>.
/// </summary>
internal static class PisumMac
{
    private const string Library = "libPisumMac";

    /// <summary>
    /// Receives the answer to the notification permission request, on a background thread.
    /// </summary>
    /// <param name="context">The context passed to <see cref="NotificationsStart"/>.</param>
    /// <param name="status">0 allowed, 2 refused, 3 failed.</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void NotificationsStartCallback(nint context, int status);

    /// <summary>
    /// The version of the helper's ABI.
    /// </summary>
    /// <returns>The version.</returns>
    [DllImport(Library, EntryPoint = "pisum_abi_version")]
    public static extern int AbiVersion();

    /// <summary>
    /// The process ID of the sender of the Apple event being handled, such as the quit event. Call it on the main
    /// thread.
    /// </summary>
    /// <returns>The process ID, or 0 without a current event or sender.</returns>
    [DllImport(Library, EntryPoint = "pisum_current_quit_sender_pid")]
    public static extern int CurrentQuitSenderPid();

    /// <summary>
    /// Sets the notification delegate and asks for permission to show alerts with sound. Call it on the main thread.
    /// </summary>
    /// <param name="callback">Receives the answer later, unless the result is 1. Keep it referenced.</param>
    /// <param name="context">Passed to the callback.</param>
    /// <returns>0 when asked, or 1 when the process doesn't run as an app bundle.</returns>
    [DllImport(Library, EntryPoint = "pisum_notifications_start")]
    public static extern int NotificationsStart(NotificationsStartCallback callback, nint context);

    /// <summary>
    /// Adds a notification with the default sound and no actions. Call it on the main thread.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="body">The text.</param>
    /// <returns>0 when added, or 1 when the process doesn't run as an app bundle.</returns>
    [DllImport(Library, EntryPoint = "pisum_notify")]
    public static extern int Notify([MarshalAs(UnmanagedType.LPUTF8Str)] string title,
                                    [MarshalAs(UnmanagedType.LPUTF8Str)] string body);

    /// <summary>
    /// The name of a process, from libc's <c>proc_name</c>.
    /// </summary>
    /// <param name="pid">The process ID.</param>
    /// <returns>The name, or <see langword="null"/> if the process doesn't exist.</returns>
    public static string? ProcessName(int pid)
    {
        // proc_name holds up to 2 × MAXCOMLEN (16) characters.
        var buffer = new byte[64];
        var length = proc_name(pid, buffer, (uint) buffer.Length);
        return length > 0 ? System.Text.Encoding.UTF8.GetString(buffer, 0, length) : null;
    }

    [DllImport("libc")]
    private static extern int proc_name(int pid, byte[] buffer, uint bufferSize);
}
