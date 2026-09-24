using System.Runtime.InteropServices;

namespace Pisum.Transcribe.Recording;

/// <summary>
/// Reads the key state on macOS (design D2 of add-macos-recording): a key counts as held while the HID system state
/// reads it as down, the session is on the console and the screen isn't locked. So the missed-release check also
/// cancels a hotkey that is still held behind the lock screen or after a switch to another user.
/// </summary>
/// <remarks>
/// The HID system state reads the physical keys, so it also sees a release while secure input hides key events from
/// the hook.
/// </remarks>
internal sealed class MacHotkeyKeyState : IHotkeyKeyState
{
    private const string CoreGraphicsPath = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundationPath = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // kCGEventSourceStateHIDSystemState
    private const int HidSystemState = 1;

    // kCFStringEncodingUTF8
    private const uint Utf8Encoding = 0x08000100;

    // kCGSessionOnConsoleKey is documented. CGSSessionScreenIsLocked isn't, and is present only while the screen is
    // locked.
    private static readonly Lazy<nint> OnConsoleKey = new(() => CreateString("kCGSSessionOnConsoleKey"));
    private static readonly Lazy<nint> ScreenIsLockedKey = new(() => CreateString("CGSSessionScreenIsLocked"));

    private readonly Func<int, bool> _isKeyDown;
    private readonly Func<SessionState> _readSession;

    /// <summary>
    /// Initializes a new instance on CoreGraphics.
    /// </summary>
    public MacHotkeyKeyState()
        : this(IsKeyDown, ReadSession)
    {
    }

    /// <summary>
    /// Initializes a new instance with other readers, for tests.
    /// </summary>
    /// <param name="isKeyDown">Reads whether a key is down by its macOS key code.</param>
    /// <param name="readSession">Reads the state of the session.</param>
    internal MacHotkeyKeyState(Func<int, bool> isKeyDown, Func<SessionState> readSession)
    {
        _isKeyDown = isKeyDown;
        _readSession = readSession;
    }

    /// <inheritdoc />
    public bool IsHeld(int rawCode)
    {
        return _isKeyDown(rawCode) && _readSession() is {IsOnConsole: true, IsScreenLocked: false};
    }

    /// <summary>
    /// Reads whether a key is physically down, from the HID system state.
    /// </summary>
    /// <param name="keyCode">The macOS key code, which is SharpHook's raw code on macOS.</param>
    /// <returns><see langword="true"/> if the key is down.</returns>
    internal static bool IsKeyDown(int keyCode)
    {
        return CGEventSourceKeyState(HidSystemState, (ushort) keyCode);
    }

    /// <summary>
    /// Reads whether the process's session is on the console and whether its screen is locked.
    /// </summary>
    /// <returns>
    /// The state. Without a session dictionary, the session counts as off the console. A missing key counts as on the
    /// console and not locked, so a key that Apple drops never cancels every hold.
    /// </returns>
    internal static SessionState ReadSession()
    {
        var session = CGSessionCopyCurrentDictionary();
        if (session == 0)
        {
            return new SessionState(false, false);
        }

        try
        {
            return new SessionState(ReadBoolean(session, OnConsoleKey.Value) ?? true,
                ReadBoolean(session, ScreenIsLockedKey.Value) ?? false);
        }
        finally
        {
            CFRelease(session);
        }
    }

    private static bool? ReadBoolean(nint dictionary, nint key)
    {
        var value = CFDictionaryGetValue(dictionary, key);
        return value == 0 ? null : CFBooleanGetValue(value);
    }

    private static nint CreateString(string text)
    {
        // Kept for the process, as a constant.
        return CFStringCreateWithCString(0, text, Utf8Encoding);
    }

    [DllImport(CoreGraphicsPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CGEventSourceKeyState(int stateId, ushort key);

    [DllImport(CoreGraphicsPath)]
    private static extern nint CGSessionCopyCurrentDictionary();

    [DllImport(CoreFoundationPath)]
    private static extern nint CFDictionaryGetValue(nint dictionary, nint key);

    [DllImport(CoreFoundationPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFBooleanGetValue(nint boolean);

    [DllImport(CoreFoundationPath)]
    private static extern nint CFStringCreateWithCString(nint allocator,
                                                         [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
                                                         uint encoding);

    [DllImport(CoreFoundationPath)]
    private static extern void CFRelease(nint reference);

    /// <summary>
    /// The state of the process's login session.
    /// </summary>
    /// <param name="IsOnConsole">Whether the session is the one on the display, not one switched away from.</param>
    /// <param name="IsScreenLocked">Whether the screen is locked.</param>
    internal readonly record struct SessionState(bool IsOnConsole, bool IsScreenLocked);
}
