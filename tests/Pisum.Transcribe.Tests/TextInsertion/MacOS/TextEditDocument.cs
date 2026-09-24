using System.Diagnostics;
using System.Runtime.InteropServices;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.TextInsertion;

/// <summary>
/// A plain text document of the tests' own in TextEdit, focused and read through the Accessibility API. Needs the
/// Accessibility grant of the terminal or IDE that runs the tests. On dispose the document window is closed, which
/// saves it to its temporary file, and the file is deleted.
/// </summary>
internal sealed class TextEditDocument : IDisposable
{
    private static readonly TimeSpan FocusTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly TempDirectory _directory = new();

    private TextEditDocument()
    {
        Directory.CreateDirectory(_directory.Path);
        Path = System.IO.Path.Combine(_directory.Path, $"pisum-{Guid.NewGuid():N}.txt");
    }

    public string Path { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>
    /// Opens a new empty document in TextEdit and waits until its text area has the keyboard focus.
    /// </summary>
    public static async Task<TextEditDocument> OpenAsync()
    {
        var document = new TextEditDocument();
        await File.WriteAllTextAsync(document.Path, string.Empty);
        await document.FocusAsync();
        return document;
    }

    /// <summary>
    /// The text of the focused text area, or <see langword="null"/> when TextEdit's document isn't focused.
    /// </summary>
    public string? ReadText()
    {
        using var element = FocusedTextArea();
        return element is null ? null : Accessibility.CopyString(element.Handle, "AXValue");
    }

    /// <summary>
    /// Waits until the document holds <paramref name="expected"/>, and returns what it held last.
    /// </summary>
    public async Task<string?> WaitForTextAsync(string expected, TimeSpan timeout)
    {
        var started = Stopwatch.StartNew();
        string? text;
        while ((text = ReadText()) != expected && started.Elapsed < timeout)
        {
            await Task.Delay(PollInterval);
        }

        return text;
    }

    /// <summary>
    /// Opens the document in TextEdit, or brings it to the front again, and waits until its text area has the focus.
    /// </summary>
    public async Task FocusAsync()
    {
        using (var open = Process.Start("open", ["-a", "TextEdit", Path]))
        {
            await open.WaitForExitAsync();
        }

        await WaitUntilFocusedAsync();
    }

    public void Dispose()
    {
        using (var window = FindWindow())
        {
            if (window is not null)
            {
                using var closeButton = Accessibility.CopyElement(window.Handle, "AXCloseButton");
                if (closeButton is not null)
                {
                    Accessibility.Press(closeButton.Handle);
                }
            }
        }

        // TextEdit writes the file as it closes the window.
        Thread.Sleep(500);
        _directory.Dispose();
    }

    private async Task WaitUntilFocusedAsync()
    {
        var started = Stopwatch.StartNew();
        while (started.Elapsed < FocusTimeout)
        {
            using (var element = FocusedTextArea())
            {
                if (element is not null)
                {
                    return;
                }
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException($"TextEdit didn't focus {FileName} within {FocusTimeout.TotalSeconds} s.");
    }

    /// <summary>
    /// The focused text area, when the focused application is TextEdit and its focused window is this document.
    /// </summary>
    private Accessibility.Element? FocusedTextArea()
    {
        using var application = Accessibility.FocusedApplication();
        if (application is null || PisumMac.ProcessName(Accessibility.GetPid(application.Handle)) != "TextEdit")
        {
            return null;
        }

        using var window = Accessibility.CopyElement(application.Handle, "AXFocusedWindow");
        if (window is null || Accessibility.CopyString(window.Handle, "AXTitle")?.StartsWith(
                System.IO.Path.GetFileNameWithoutExtension(Path), StringComparison.Ordinal) != true)
        {
            return null;
        }

        var element = Accessibility.CopyElement(application.Handle, "AXFocusedUIElement");
        if (element is not null && Accessibility.CopyString(element.Handle, "AXRole") == "AXTextArea")
        {
            return element;
        }

        element?.Dispose();
        return null;
    }

    private Accessibility.Element? FindWindow()
    {
        using var application = Accessibility.FocusedApplication();
        if (application is null || PisumMac.ProcessName(Accessibility.GetPid(application.Handle)) != "TextEdit")
        {
            return null;
        }

        var window = Accessibility.CopyElement(application.Handle, "AXFocusedWindow");
        if (window is not null && Accessibility.CopyString(window.Handle, "AXTitle")?.StartsWith(
                System.IO.Path.GetFileNameWithoutExtension(Path), StringComparison.Ordinal) == true)
        {
            return window;
        }

        window?.Dispose();
        return null;
    }

    /// <summary>
    /// The parts of the Accessibility and CoreFoundation C APIs that the tests read TextEdit with.
    /// </summary>
    internal static class Accessibility
    {
        private const string CoreFoundationPath = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        private const string ApplicationServicesPath =
            "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

        // kCFStringEncodingUTF8
        private const uint StringEncodingUtf8 = 0x08000100;

        public static Element? FocusedApplication()
        {
            var systemWide = AXUIElementCreateSystemWide();
            try
            {
                return CopyElement(systemWide, "AXFocusedApplication");
            }
            finally
            {
                CFRelease(systemWide);
            }
        }

        public static int GetPid(nint element)
        {
            return AXUIElementGetPid(element, out var pid) == 0 ? pid : 0;
        }

        public static Element? CopyElement(nint element, string attribute)
        {
            var value = CopyAttribute(element, attribute);
            return value == 0 ? null : new Element(value);
        }

        public static string? CopyString(nint element, string attribute)
        {
            var value = CopyAttribute(element, attribute);
            if (value == 0)
            {
                return null;
            }

            try
            {
                if (CFGetTypeID(value) != CFStringGetTypeID())
                {
                    return null;
                }

                var length = CFStringGetLength(value);
                var characters = new char[length];
                CFStringGetCharacters(value, new CFRange {Location = 0, Length = length}, characters);
                return new string(characters);
            }
            finally
            {
                CFRelease(value);
            }
        }

        public static void Press(nint element)
        {
            var action = CFStringCreateWithCString(0, "AXPress", StringEncodingUtf8);
            try
            {
                AXUIElementPerformAction(element, action);
            }
            finally
            {
                CFRelease(action);
            }
        }

        private static nint CopyAttribute(nint element, string attribute)
        {
            var name = CFStringCreateWithCString(0, attribute, StringEncodingUtf8);
            try
            {
                return AXUIElementCopyAttributeValue(element, name, out var value) == 0 ? value : 0;
            }
            finally
            {
                CFRelease(name);
            }
        }

        [DllImport(ApplicationServicesPath)]
        private static extern nint AXUIElementCreateSystemWide();

        [DllImport(ApplicationServicesPath)]
        private static extern int AXUIElementCopyAttributeValue(nint element, nint attribute, out nint value);

        [DllImport(ApplicationServicesPath)]
        private static extern int AXUIElementGetPid(nint element, out int pid);

        [DllImport(ApplicationServicesPath)]
        private static extern int AXUIElementPerformAction(nint element, nint action);

        [DllImport(CoreFoundationPath)]
        private static extern nint CFStringCreateWithCString(nint allocator,
                                                             [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
                                                             uint encoding);

        [DllImport(CoreFoundationPath)]
        private static extern nint CFStringGetLength(nint text);

        [DllImport(CoreFoundationPath, CharSet = CharSet.Unicode)]
        private static extern void CFStringGetCharacters(nint text, CFRange range, [Out] char[] buffer);

        [DllImport(CoreFoundationPath)]
        private static extern nuint CFGetTypeID(nint reference);

        [DllImport(CoreFoundationPath)]
        private static extern nuint CFStringGetTypeID();

        [DllImport(CoreFoundationPath)]
        private static extern void CFRelease(nint reference);

        [StructLayout(LayoutKind.Sequential)]
        private struct CFRange
        {
            public nint Location;
            public nint Length;
        }

        /// <summary>
        /// A retained Accessibility element, released on dispose.
        /// </summary>
        public sealed class Element : IDisposable
        {
            public Element(nint handle)
            {
                Handle = handle;
            }

            public nint Handle { get; }

            public void Dispose()
            {
                CFRelease(Handle);
            }
        }
    }
}
