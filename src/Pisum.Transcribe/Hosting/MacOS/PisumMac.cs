using System.Runtime.InteropServices;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// The functions of the Swift helper <c>libPisumMac.dylib</c> (design D3 of add-macos-shell and of add-macos-setup),
/// and the libc functions that go with them. Call the helper's functions only while
/// <see cref="MacNativeLibrary.IsAvailable"/> is <see langword="true"/>, and on the UI thread, except
/// <see cref="PasteboardProbe"/> and the text insertion's pasteboard functions, which run on its pasteboard thread
/// (design D2 of add-macos-text-insertion).
/// </summary>
internal static class PisumMac
{
    private const string Library = "libPisumMac";

    /// <summary>
    /// Receives the result of an asynchronous helper function, on a background thread.
    /// </summary>
    /// <param name="context">The context passed to the function.</param>
    /// <param name="status">The function's status code.</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void StatusCallback(nint context, int status);

    /// <summary>
    /// The one callback for every asynchronous helper function. Its context is a handle from <see cref="CreateContext"/>,
    /// and the callback frees it and then calls the handle's action.
    /// </summary>
    public static readonly StatusCallback Callback = OnCallback;

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
    /// Whether the process runs as an app bundle with a bundle identifier.
    /// </summary>
    /// <returns>1 in an app bundle, otherwise 0.</returns>
    [DllImport(Library, EntryPoint = "pisum_has_bundle")]
    public static extern int HasBundle();

    /// <summary>
    /// Sets the notification delegate, without asking for permission. Call it on the main thread.
    /// </summary>
    /// <returns>0, or 1 when the process doesn't run as an app bundle.</returns>
    [DllImport(Library, EntryPoint = "pisum_notifications_start")]
    public static extern int NotificationsStart();

    /// <summary>
    /// Asks for permission to show alerts with sound. macOS asks the user once and remembers the answer.
    /// </summary>
    /// <param name="callback"><see cref="Callback"/>, which receives 0 allowed, 2 refused or 3 failed, unless the result is 1.</param>
    /// <param name="context">A context from <see cref="CreateContext"/>. Free it when the result is 1.</param>
    /// <returns>0 when asked, or 1 when the process doesn't run as an app bundle.</returns>
    [DllImport(Library, EntryPoint = "pisum_notifications_request")]
    public static extern int NotificationsRequest(StatusCallback callback, nint context);

    /// <summary>
    /// Reads whether the user allowed notifications.
    /// </summary>
    /// <param name="callback">
    /// <see cref="Callback"/>, which receives 0 not answered yet, 2 refused or 3 allowed, unless the result is 1.
    /// </param>
    /// <param name="context">A context from <see cref="CreateContext"/>. Free it when the result is 1.</param>
    /// <returns>0 when read, or 1 when the process doesn't run as an app bundle.</returns>
    [DllImport(Library, EntryPoint = "pisum_notifications_status")]
    public static extern int NotificationsStatus(StatusCallback callback, nint context);

    /// <summary>
    /// The microphone permission.
    /// </summary>
    /// <returns>0 not asked yet, 1 restricted, 2 denied, 3 allowed.</returns>
    [DllImport(Library, EntryPoint = "pisum_microphone_status")]
    public static extern int MicrophoneStatus();

    /// <summary>
    /// Asks for the microphone permission. macOS shows its prompt only while the permission wasn't asked yet.
    /// </summary>
    /// <param name="callback"><see cref="Callback"/>, which receives the new status of <see cref="MicrophoneStatus"/>.</param>
    /// <param name="context">A context from <see cref="CreateContext"/>.</param>
    [DllImport(Library, EntryPoint = "pisum_microphone_request")]
    public static extern void MicrophoneRequest(StatusCallback callback, nint context);

    /// <summary>
    /// How the system lets the app read the general pasteboard.
    /// </summary>
    /// <returns>-1 before macOS 15.4, otherwise 0 default, 1 asks each time, 2 always allowed, 3 always denied.</returns>
    [DllImport(Library, EntryPoint = "pisum_pasteboard_access_behavior")]
    public static extern int PasteboardAccessBehavior();

    /// <summary>
    /// Reads the general pasteboard once and discards it, so that macOS asks the user now. <b>Never call it on the UI
    /// thread</b>: macOS's alert blocks the reading thread until the user answers. The only helper function that
    /// must not run there.
    /// </summary>
    /// <returns>0.</returns>
    [DllImport(Library, EntryPoint = "pisum_pasteboard_probe")]
    public static extern int PasteboardProbe();

    /// <summary>
    /// The pasteboard's change count, which rises with every write. Never alerts.
    /// </summary>
    /// <param name="name">The pasteboard's name, or <see langword="null"/> for the general pasteboard.</param>
    /// <returns>The change count.</returns>
    [DllImport(Library, EntryPoint = "pisum_pasteboard_change_count")]
    public static extern long PasteboardChangeCount([MarshalAs(UnmanagedType.LPUTF8Str)] string? name);

    /// <summary>
    /// Copies every item of the pasteboard with every type except file promises into a buffer. Reads the pasteboard,
    /// so check <see cref="PasteboardAccessBehavior"/> first for the general pasteboard.
    /// </summary>
    /// <param name="name">The pasteboard's name, or <see langword="null"/> for the general pasteboard.</param>
    /// <param name="buffer">The buffer, to free with <see cref="Free"/> when the result is 0.</param>
    /// <param name="length">The buffer's length in bytes.</param>
    /// <returns>0, or 1 when the buffer couldn't be allocated.</returns>
    [DllImport(Library, EntryPoint = "pisum_pasteboard_snapshot")]
    public static extern int PasteboardSnapshot([MarshalAs(UnmanagedType.LPUTF8Str)] string? name,
                                                out nint buffer,
                                                out long length);

    /// <summary>
    /// Replaces the pasteboard's contents with text.
    /// </summary>
    /// <param name="name">The pasteboard's name, or <see langword="null"/> for the general pasteboard.</param>
    /// <param name="text">The text.</param>
    /// <param name="exclude">
    /// 1 to keep the text on this Mac and mark it for clipboard managers as transient, concealed and auto-generated.
    /// </param>
    /// <returns>0, or 1 when it wasn't written.</returns>
    [DllImport(Library, EntryPoint = "pisum_pasteboard_set_text")]
    public static extern int PasteboardSetText([MarshalAs(UnmanagedType.LPUTF8Str)] string? name,
                                               [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
                                               int exclude);

    /// <summary>
    /// Replaces the pasteboard's contents with the items of a snapshot buffer. An empty snapshot clears it.
    /// </summary>
    /// <param name="name">The pasteboard's name, or <see langword="null"/> for the general pasteboard.</param>
    /// <param name="buffer">The buffer, in the format of <see cref="PasteboardSnapshot"/>.</param>
    /// <param name="length">The buffer's length in bytes.</param>
    /// <param name="mark">
    /// 1 for a restore: the content stays on this Mac and carries the transient and restored markers. 0 writes the
    /// items as they are, for tests.
    /// </param>
    /// <returns>0, 1 when it wasn't written, or 2 for a malformed buffer.</returns>
    [DllImport(Library, EntryPoint = "pisum_pasteboard_restore")]
    public static extern int PasteboardRestore([MarshalAs(UnmanagedType.LPUTF8Str)] string? name,
                                               byte[] buffer,
                                               long length,
                                               int mark);

    /// <summary>
    /// Releases a named pasteboard, for tests.
    /// </summary>
    /// <param name="name">The pasteboard's name.</param>
    /// <returns>0, or 1 for <see langword="null"/>, the general pasteboard, which is never released.</returns>
    [DllImport(Library, EntryPoint = "pisum_pasteboard_release")]
    public static extern int PasteboardRelease([MarshalAs(UnmanagedType.LPUTF8Str)] string? name);

    /// <summary>
    /// Frees memory that a helper function allocated for the caller.
    /// </summary>
    /// <param name="pointer">The memory.</param>
    [DllImport(Library, EntryPoint = "pisum_free")]
    public static extern void Free(nint pointer);

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

    /// <summary>
    /// Creates the context for <see cref="Callback"/>, which calls <paramref name="completed"/> once with the status.
    /// </summary>
    /// <param name="completed">Receives the status on a background thread.</param>
    /// <returns>The context. Free it with <see cref="FreeContext"/> when the helper doesn't call back.</returns>
    public static nint CreateContext(Action<int> completed)
    {
        return GCHandle.ToIntPtr(GCHandle.Alloc(completed));
    }

    /// <summary>
    /// Frees a context from <see cref="CreateContext"/> that the helper didn't call back with.
    /// </summary>
    /// <param name="context">The context.</param>
    public static void FreeContext(nint context)
    {
        GCHandle.FromIntPtr(context).Free();
    }

    [DllImport("libc")]
    private static extern int proc_name(int pid, byte[] buffer, uint bufferSize);

    private static void OnCallback(nint context, int status)
    {
        var handle = GCHandle.FromIntPtr(context);
        var completed = (Action<int>) handle.Target!;
        handle.Free();
        completed(status);
    }
}
