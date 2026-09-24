using System.Runtime.InteropServices;

namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// Keyboard events posted with CoreGraphics at the HID event tap (design D4 of add-macos-text-insertion). The window
/// server stamps this process's ID on every posted event, so the keyboard hook reports them as simulated.
/// </summary>
internal sealed class CoreGraphicsKeyEvents : IMacKeyEvents
{
    private const string CoreGraphicsPath = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundationPath = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // kCGHIDEventTap
    private const int HidEventTap = 0;

    // kCGEventSourceStateHIDSystemState
    private const int HidSystemState = 1;

    /// <inheritdoc />
    public bool CanPost()
    {
        // A cheap check, safe on any thread. It stays false after a grant made while the process runs (spike M3).
        return CGPreflightPostEventAccess();
    }

    /// <inheritdoc />
    public void PostKey(ushort keyCode, bool down, ulong flags)
    {
        var keyEvent = CGEventCreateKeyboardEvent(0, keyCode, down);
        if (keyEvent == 0)
        {
            return;
        }

        try
        {
            CGEventSetFlags(keyEvent, flags);
            CGEventPost(HidEventTap, keyEvent);
        }
        finally
        {
            CFRelease(keyEvent);
        }
    }

    /// <inheritdoc />
    public void PostText(string text)
    {
        // Key code 0 as libuiohook and most typing tools use; the application reads the string instead.
        PostTextEvent(text, true);
        PostTextEvent(text, false);
    }

    /// <inheritdoc />
    public ulong ReadFlags()
    {
        return CGEventSourceFlagsState(HidSystemState);
    }

    private static void PostTextEvent(string text, bool down)
    {
        var keyEvent = CGEventCreateKeyboardEvent(0, 0, down);
        if (keyEvent == 0)
        {
            return;
        }

        try
        {
            CGEventSetFlags(keyEvent, 0);
            CGEventKeyboardSetUnicodeString(keyEvent, (nuint) text.Length, text);
            CGEventPost(HidEventTap, keyEvent);
        }
        finally
        {
            CFRelease(keyEvent);
        }
    }

    [DllImport(CoreGraphicsPath)]
    private static extern nint CGEventCreateKeyboardEvent(nint source,
                                                          ushort virtualKey,
                                                          [MarshalAs(UnmanagedType.U1)] bool keyDown);

    [DllImport(CoreGraphicsPath)]
    private static extern void CGEventSetFlags(nint keyEvent, ulong flags);

    [DllImport(CoreGraphicsPath, CharSet = CharSet.Unicode)]
    private static extern void CGEventKeyboardSetUnicodeString(nint keyEvent, nuint length, string text);

    [DllImport(CoreGraphicsPath)]
    private static extern void CGEventPost(int tap, nint keyEvent);

    [DllImport(CoreGraphicsPath)]
    private static extern ulong CGEventSourceFlagsState(int stateId);

    [DllImport(CoreGraphicsPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CGPreflightPostEventAccess();

    [DllImport(CoreFoundationPath)]
    private static extern void CFRelease(nint reference);
}
