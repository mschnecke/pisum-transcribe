using System.Runtime.InteropServices;

namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// Finds the frontmost application from the window server's window list, for when the Accessibility API names no
/// focused application, as for Electron apps (design D11 of add-macos-dictation). Safe on any thread.
/// </summary>
internal static class FrontmostApplication
{
    private const string CoreGraphicsPath = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundationPath = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements
    private const uint OnScreenWithoutDesktop = 1 | 16;

    // kCFNumberSInt32Type and kCFNumberFloat64Type
    private const int SInt32Type = 3;
    private const int Float64Type = 6;

    private static readonly Lazy<(nint Layer, nint OwnerPid, nint Alpha)> Keys = new(ReadKeys);

    /// <summary>
    /// Picks the owner of the frontmost normal window: the first window at layer 0 that is visible and doesn't belong to
    /// this process. Layer 0 leaves out floating panels, the menu bar, notification banners and the recording overlay.
    /// </summary>
    /// <param name="windows">The on-screen windows in the window server's front-to-back order.</param>
    /// <param name="ownProcessId">This process, whose own windows are no dictation target here.</param>
    /// <returns>The owner's process identifier, or <see langword="null"/> without such a window.</returns>
    public static int? Choose(IEnumerable<(int ProcessId, int Layer, double Alpha)> windows, int ownProcessId)
    {
        foreach (var window in windows)
        {
            if (window is {Layer: 0, Alpha: > 0} && window.ProcessId != ownProcessId)
            {
                return window.ProcessId;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the window list and picks the owner of the frontmost normal window with <see cref="Choose"/>.
    /// </summary>
    /// <returns>The owner's process identifier, or <see langword="null"/> without such a window.</returns>
    public static int? Find()
    {
        var list = CGWindowListCopyWindowInfo(OnScreenWithoutDesktop, 0);
        if (list == 0)
        {
            return null;
        }

        try
        {
            return Choose(ReadWindows(list), Environment.ProcessId);
        }
        finally
        {
            CFRelease(list);
        }
    }

    private static IEnumerable<(int ProcessId, int Layer, double Alpha)> ReadWindows(nint list)
    {
        var keys = Keys.Value;
        var count = CFArrayGetCount(list);
        for (nint index = 0; index < count; index++)
        {
            // Borrowed references: the array keeps its dictionaries and their values alive.
            var window = CFArrayGetValueAtIndex(list, index);
            if (TryReadInt(window, keys.OwnerPid, out var processId) && TryReadInt(window, keys.Layer, out var layer))
            {
                var alpha = TryReadDouble(window, keys.Alpha, out var value) ? value : 1;
                yield return (processId, layer, alpha);
            }
        }
    }

    private static bool TryReadInt(nint dictionary, nint key, out int value)
    {
        value = 0;
        var number = CFDictionaryGetValue(dictionary, key);
        return number != 0 && CFNumberGetValue(number, SInt32Type, out value);
    }

    private static bool TryReadDouble(nint dictionary, nint key, out double value)
    {
        value = 0;
        var number = CFDictionaryGetValue(dictionary, key);
        return number != 0 && CFNumberGetValue(number, Float64Type, out value);
    }

    private static (nint, nint, nint) ReadKeys()
    {
        // kCGWindowLayer, kCGWindowOwnerPID and kCGWindowAlpha are exported CFStringRef constants.
        var library = NativeLibrary.Load(CoreGraphicsPath);
        return (Read("kCGWindowLayer"), Read("kCGWindowOwnerPID"), Read("kCGWindowAlpha"));

        nint Read(string name)
        {
            return Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));
        }
    }

    [DllImport(CoreGraphicsPath)]
    private static extern nint CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [DllImport(CoreFoundationPath)]
    private static extern nint CFArrayGetCount(nint array);

    [DllImport(CoreFoundationPath)]
    private static extern nint CFArrayGetValueAtIndex(nint array, nint index);

    [DllImport(CoreFoundationPath)]
    private static extern nint CFDictionaryGetValue(nint dictionary, nint key);

    [DllImport(CoreFoundationPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFNumberGetValue(nint number, int type, out int value);

    [DllImport(CoreFoundationPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFNumberGetValue(nint number, int type, out double value);

    [DllImport(CoreFoundationPath)]
    private static extern void CFRelease(nint reference);
}
