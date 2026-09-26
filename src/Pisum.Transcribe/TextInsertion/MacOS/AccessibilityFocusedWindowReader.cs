using System.Runtime.InteropServices;
using Avalonia;
using Microsoft.Extensions.Logging;

namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// The focused window through the Accessibility C API (design D1 of add-macos-text-insertion). Needs the Accessibility
/// grant; without it every read fails, and the target counts as no window.
/// </summary>
internal sealed class AccessibilityFocusedWindowReader : IFocusedWindowReader, IDisposable
{
    /// <summary>
    /// How long an application may take to answer, for every Accessibility call of the process. A hung application
    /// costs at most this much per call.
    /// </summary>
    public const float MessagingTimeoutSeconds = 0.25f;

    private const string CoreFoundationPath = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const string ApplicationServicesPath =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    // kCFStringEncodingUTF8
    private const uint StringEncodingUtf8 = 0x08000100;

    // kAXErrorNoValue
    private const int NoValueError = -25212;

    // kAXValueCGPointType and kAXValueCGSizeType
    private const int CGPointType = 1;
    private const int CGSizeType = 2;

    private readonly ILogger<AccessibilityFocusedWindowReader> _logger;
    private readonly nint _systemWide;
    private readonly nint _focusedApplicationAttribute;
    private readonly nint _focusedWindowAttribute;
    private readonly nint _positionAttribute;
    private readonly nint _sizeAttribute;

    /// <summary>
    /// Initializes a new instance and sets the messaging timeout.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public AccessibilityFocusedWindowReader(ILogger<AccessibilityFocusedWindowReader> logger)
    {
        _logger = logger;
        _systemWide = AXUIElementCreateSystemWide();

        // Set on the system-wide element, the timeout applies to every element.
        AXUIElementSetMessagingTimeout(_systemWide, MessagingTimeoutSeconds);

        // kAXFocusedApplicationAttribute and kAXFocusedWindowAttribute are CFSTR macros, not exported symbols.
        _focusedApplicationAttribute = CFStringCreateWithCString(0, "AXFocusedApplication", StringEncodingUtf8);
        _focusedWindowAttribute = CFStringCreateWithCString(0, "AXFocusedWindow", StringEncodingUtf8);
        _positionAttribute = CFStringCreateWithCString(0, "AXPosition", StringEncodingUtf8);
        _sizeAttribute = CFStringCreateWithCString(0, "AXSize", StringEncodingUtf8);
    }

    /// <inheritdoc />
    public bool TryRead(out int processId, out nint window)
    {
        processId = 0;
        window = 0;
        var error = AXUIElementCopyAttributeValue(_systemWide, _focusedApplicationAttribute, out var application);

        // macOS names no focused application for Electron apps until their accessibility is switched on, or while a
        // menu is open. Their own element still answers (design D11 of add-macos-dictation).
        if (error == NoValueError && FrontmostApplication.Find() is { } frontmost)
        {
            _logger.LogInformation("No focused application is named, so process {ProcessId} of the frontmost window is used",
                frontmost);
            application = AXUIElementCreateApplication(frontmost);
            error = application == 0 ? NoValueError : 0;
        }

        if (error != 0)
        {
            _logger.LogInformation("The focused application could not be read (AX error {Error})", error);
            return false;
        }

        try
        {
            error = AXUIElementGetPid(application, out processId);
            if (error == 0)
            {
                error = AXUIElementCopyAttributeValue(application, _focusedWindowAttribute, out window);
            }

            if (error != 0)
            {
                _logger.LogInformation("The focused window of process {ProcessId} could not be read (AX error {Error})",
                    processId, error);
                window = 0;
                return false;
            }

            return true;
        }
        finally
        {
            CFRelease(application);
        }
    }

    /// <inheritdoc />
    public bool AreSameWindow(nint first, nint second)
    {
        return CFEqual(first, second);
    }

    /// <inheritdoc />
    public bool TryReadFrame(nint window, out Rect frame)
    {
        frame = default;
        if (!TryReadPair(window, _positionAttribute, CGPointType, out var x, out var y) ||
            !TryReadPair(window, _sizeAttribute, CGSizeType, out var width, out var height))
        {
            _logger.LogInformation("The frame of the focused window could not be read");
            return false;
        }

        frame = new Rect(x, y, width, height);
        return true;
    }

    /// <inheritdoc />
    public void Release(nint window)
    {
        CFRelease(window);
    }

    /// <summary>
    /// Releases the system-wide element and the attribute names.
    /// </summary>
    public void Dispose()
    {
        CFRelease(_sizeAttribute);
        CFRelease(_positionAttribute);
        CFRelease(_focusedWindowAttribute);
        CFRelease(_focusedApplicationAttribute);
        CFRelease(_systemWide);
    }

    /// <summary>
    /// Reads an attribute whose value is a CGPoint or a CGSize, both two doubles.
    /// </summary>
    private static bool TryReadPair(nint element, nint attribute, int type, out double first, out double second)
    {
        first = 0;
        second = 0;
        if (AXUIElementCopyAttributeValue(element, attribute, out var value) != 0)
        {
            return false;
        }

        try
        {
            var pair = new double[2];
            if (!AXValueGetValue(value, type, pair))
            {
                return false;
            }

            first = pair[0];
            second = pair[1];
            return true;
        }
        finally
        {
            CFRelease(value);
        }
    }

    [DllImport(ApplicationServicesPath)]
    private static extern nint AXUIElementCreateSystemWide();

    [DllImport(ApplicationServicesPath)]
    private static extern nint AXUIElementCreateApplication(int processId);

    [DllImport(ApplicationServicesPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool AXValueGetValue(nint value, int type, [Out] double[] result);

    [DllImport(ApplicationServicesPath)]
    private static extern int AXUIElementSetMessagingTimeout(nint element, float timeoutInSeconds);

    [DllImport(ApplicationServicesPath)]
    private static extern int AXUIElementCopyAttributeValue(nint element, nint attribute, out nint value);

    [DllImport(ApplicationServicesPath)]
    private static extern int AXUIElementGetPid(nint element, out int processId);

    [DllImport(CoreFoundationPath)]
    private static extern nint CFStringCreateWithCString(nint allocator,
                                                         [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
                                                         uint encoding);

    [DllImport(CoreFoundationPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFEqual(nint first, nint second);

    [DllImport(CoreFoundationPath)]
    private static extern void CFRelease(nint reference);
}
