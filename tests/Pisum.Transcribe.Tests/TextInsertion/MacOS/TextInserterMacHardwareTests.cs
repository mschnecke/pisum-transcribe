using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.Tests.Hosting;
using Pisum.Transcribe.TextInsertion;
using SharpHook;
using SharpHook.Data;
using SharpHook.Providers;

namespace Pisum.Transcribe.Tests.TextInsertion;

/// <summary>
/// The macOS text insertion against TextEdit, with the general pasteboard, real keystrokes and the Accessibility API.
/// Needs the Accessibility grant of the terminal or IDE that runs the tests, and skips without it. Don't touch the
/// keyboard or mouse while they run: the keystrokes go to whatever has the focus.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Hardware)]
[Collection(DesktopCollection.Name)]
public sealed class TextInserterMacHardwareTests : IAsyncLifetime
{
    private const string Transcript = "Grüße aus Köln – 5 €";

    private static readonly TimeSpan InputTimeout = TimeSpan.FromSeconds(5);

    private readonly MacClipboardService _clipboard =
        new(new MacPasteboard(null), true, NullLogger<MacClipboardService>.Instance);

    private readonly MacKeyboardInput _keyboard = new(new CoreGraphicsKeyEvents(), new MacKeyboardLayout(),
        new InlineUiDispatcher(), NullLogger<MacKeyboardInput>.Instance);

    private readonly AccessibilityFocusedWindowReader _reader =
        new(NullLogger<AccessibilityFocusedWindowReader>.Instance);

    private readonly MacForegroundWindowTracker _tracker;
    private readonly TextInserter _sut;
    private ClipboardSnapshot? _usersClipboard;

    public TextInserterMacHardwareTests()
    {
        _tracker = new MacForegroundWindowTracker(_reader);
        _sut = new TextInserter(_clipboard, _keyboard, _tracker, new MacSecureInput(), TimeProvider.System,
            NullLogger<TextInserter>.Instance, false);
    }

    private static bool IsSameWindow(Rect bounds, Rect frame)
    {
        return Math.Abs(bounds.Center.X - frame.Center.X) <= 1 && Math.Abs(bounds.Center.Y - frame.Center.Y) <= 1 &&
               Math.Abs(bounds.Width - frame.Width) <= 8 && Math.Abs(bounds.Height - frame.Height) <= 8;
    }

    private static bool MayReadPasteboard => PisumMac.PasteboardAccessBehavior() is -1 or 2;

    public async ValueTask InitializeAsync()
    {
        Assert.SkipWhen(!CoreFoundation.IsProcessTrusted(),
            "The terminal or IDE that runs the tests needs the Accessibility grant.");
        await _keyboard.StartAsync(TestContext.Current.CancellationToken);

        // Put back after the test, so the user's clipboard survives the run.
        if (MayReadPasteboard)
        {
            _usersClipboard = await _clipboard.TrySnapshotAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _sut.StopAsync(CancellationToken.None);
        if (_usersClipboard is {IsSensitive: false})
        {
            await _clipboard.TryRestoreAsync(_usersClipboard);
        }

        await _keyboard.StopAsync(CancellationToken.None);
        _tracker.Dispose();
        _reader.Dispose();
        _clipboard.Dispose();
    }

    [Fact(Explicit = true)]
    public async Task InsertAsync_PasteWithRestore_InsertsTheTranscriptAndRestoresTheClipboard()
    {
        // Arrange
        Assert.SkipUnless(MayReadPasteboard, "The pasteboard can't be read without asking, so the text is typed.");
        using var document = await TextEditDocument.OpenAsync();
        await _clipboard.TrySetTextAsync("invoice 4711", false);
        var target = _tracker.CaptureForeground();

        // Act
        var settings = new TextInsertionSettings(InsertionMethod.ClipboardPaste, true);
        var outcome = await _sut.InsertAsync(Transcript, target, settings, TestContext.Current.CancellationToken);
        var text = await document.WaitForTextAsync(Transcript, InputTimeout);
        await _sut.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBe(InsertionOutcome.Inserted);
        text.ShouldBe(Transcript);
        var restored = (await _clipboard.TrySnapshotAsync()).ShouldBeOfType<MacClipboardSnapshot>();
        restored.Items.ShouldHaveSingleItem().Single(entry => entry.Type == "public.utf8-plain-text").Data
            .ShouldBe("invoice 4711"u8.ToArray());
    }

    [Fact(Explicit = true)]
    public async Task InsertAsync_TypeLongTextWithEmojiAndLineBreaks_TypesItExactly()
    {
        // Arrange
        using var document = await TextEditDocument.OpenAsync();
        var line = string.Concat(Enumerable.Repeat("Grüße aus Köln – 5 € 👋 ", 4));
        var transcript = string.Join('\n', Enumerable.Repeat(line, 5));
        var target = _tracker.CaptureForeground();

        // Act
        var settings = new TextInsertionSettings(InsertionMethod.TypeText, true);
        var outcome = await _sut.InsertAsync(transcript, target, settings, TestContext.Current.CancellationToken);
        var text = await document.WaitForTextAsync(transcript, InputTimeout);

        // Assert
        transcript.Length.ShouldBeGreaterThan(400);
        outcome.ShouldBe(InsertionOutcome.Inserted);
        text.ShouldBe(transcript);
    }

    [Fact(Explicit = true)]
    public async Task CaptureForeground_TextEditDocument_ReadsTheFrameTheWindowServerReports()
    {
        // Arrange
        using var document = await TextEditDocument.OpenAsync();

        // Act
        var target = _tracker.CaptureForeground();
        var found = _tracker.TryGetFrame(target.Window, out var frame);

        // Assert
        // The AX frame is a few points larger than the window server's bounds on macOS 27 (146, 71, 656, 422 against
        // 148, 72, 652, 420), in the same coordinate space and with the same center, which is what the placement uses.
        found.ShouldBeTrue();
        var windows = await WindowServer.WaitForWindowsAsync(target.ProcessId,
            list => list.Any(window => window.Layer == 0 && IsSameWindow(window.Bounds, frame)));
        windows.ShouldContain(window => window.Layer == 0 && IsSameWindow(window.Bounds, frame),
            $"The frame was {frame}, and TextEdit's windows are {string.Join("; ", windows)}.");
    }

    [Fact(Explicit = true)]
    public async Task FrontmostApplicationFind_TextEditDocumentInFront_ReturnsTextEdit()
    {
        // Arrange
        using var document = await TextEditDocument.OpenAsync();

        // Act
        var processId = FrontmostApplication.Find();

        // Assert
        processId.ShouldNotBeNull();
        PisumMac.ProcessName(processId.Value).ShouldBe("TextEdit");
    }

    [Fact(Explicit = true)]
    public async Task CaptureForeground_VisualStudioCodeInFront_CapturesItsWindow()
    {
        // Arrange: an Electron app, for which macOS names no focused application until its accessibility is on.
        const string VisualStudioCode = "/Applications/Visual Studio Code.app";
        Assert.SkipUnless(Directory.Exists(VisualStudioCode), "Visual Studio Code isn't installed.");
        using (var open = Process.Start("open", ["-a", VisualStudioCode]))
        {
            await open.WaitForExitAsync(TestContext.Current.CancellationToken);
        }

        // Act: VS Code takes a moment to come to the front.
        var target = new InsertionTarget(0, 0, false);
        var started = Stopwatch.StartNew();
        while (started.Elapsed < InputTimeout &&
               (target.Window == 0 || PisumMac.ProcessName(target.ProcessId) != "Code"))
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            target = _tracker.CaptureForeground();
        }

        // Assert
        target.Window.ShouldNotBe(0);
        PisumMac.ProcessName(target.ProcessId).ShouldBe("Code");
        _tracker.IsForeground(target).ShouldBeTrue();
    }

    [Fact(Explicit = true)]
    public async Task IsForeground_AnotherTextEditDocumentFocused_ReturnsFalse()
    {
        // Arrange
        using var first = await TextEditDocument.OpenAsync();
        var target = _tracker.CaptureForeground();
        var focusedAtCapture = _tracker.IsForeground(target);
        using var second = await TextEditDocument.OpenAsync();

        // Act
        var focusedAfterSwitch = _tracker.IsForeground(target);

        // Assert
        target.Window.ShouldNotBe(0);
        focusedAtCapture.ShouldBeTrue();
        focusedAfterSwitch.ShouldBeFalse();
    }

    [Fact(Explicit = true)]
    public async Task SendPaste_WhileTheKeyboardHookRuns_IsReportedAsSimulated()
    {
        // Arrange
        using var document = await TextEditDocument.OpenAsync();
        UioHookProvider.Instance.PromptUserIfAxApiDisabled = false;
        using var hook = new SimpleGlobalHook();
        var pressed =
            new TaskCompletionSource<KeyboardHookEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        hook.KeyPressed += (_, e) =>
        {
            if (e.Data.KeyCode == KeyCode.VcV)
            {
                pressed.TrySetResult(e);
            }
        };
        var run = hook.RunAsync(GlobalHookType.Keyboard, true);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Act
        _keyboard.SendPaste();
        var e = await pressed.Task.WaitAsync(InputTimeout, TestContext.Current.CancellationToken);

        // Assert
        e.IsEventSimulated.ShouldBeTrue();
        hook.Stop();
        await run;
    }

    [Fact(Explicit = true)]
    public async Task SendPaste_DvorakLayout_PastesWithTheDvorakKeyOfV()
    {
        // Arrange
        using var layout = InputSources.Select("com.apple.keylayout.Dvorak");
        Assert.SkipWhen(layout is null, "The Dvorak input source isn't enabled.");
        var keyboard = new MacKeyboardInput(new CoreGraphicsKeyEvents(), new MacKeyboardLayout(),
            new InlineUiDispatcher(), NullLogger<MacKeyboardInput>.Instance);
        await keyboard.StartAsync(TestContext.Current.CancellationToken);
        using var document = await TextEditDocument.OpenAsync();
        await _clipboard.TrySetTextAsync(Transcript, false);

        // Act
        keyboard.SendPaste();
        var text = await document.WaitForTextAsync(Transcript, InputTimeout);

        // Assert
        keyboard.PasteKeyCode.ShouldNotBe(MacKeyboardInput.DefaultPasteKeyCode);
        text.ShouldBe(Transcript);
        await keyboard.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Selects an enabled input source and selects the previous one again on dispose.
    /// </summary>
    private sealed class InputSources : IDisposable
    {
        private const string CarbonPath = "/System/Library/Frameworks/Carbon.framework/Carbon";
        private const string CoreFoundationPath = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        private readonly nint _previous;

        private InputSources(nint previous)
        {
            _previous = previous;
        }

        public static InputSources? Select(string id)
        {
            var idKey = Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(CarbonPath),
                "kTISPropertyInputSourceID"));
            var list = TISCreateInputSourceList(0, false);
            try
            {
                for (nint i = 0; i < CFArrayGetCount(list); i++)
                {
                    var source = CFArrayGetValueAtIndex(list, i);
                    if (ReadString(TISGetInputSourceProperty(source, idKey)) != id)
                    {
                        continue;
                    }

                    var previous = TISCopyCurrentKeyboardInputSource();
                    TISSelectInputSource(source);
                    return new InputSources(previous);
                }

                return null;
            }
            finally
            {
                CFRelease(list);
            }
        }

        public void Dispose()
        {
            TISSelectInputSource(_previous);
            CFRelease(_previous);
        }

        private static string? ReadString(nint text)
        {
            if (text == 0)
            {
                return null;
            }

            var characters = new char[CFStringGetLength(text)];
            CFStringGetCharacters(text, new CFRange {Location = 0, Length = characters.Length}, characters);
            return new string(characters);
        }

        [DllImport(CarbonPath)]
        private static extern nint TISCreateInputSourceList(nint properties,
                                                            [MarshalAs(UnmanagedType.U1)] bool includeAllInstalled);

        [DllImport(CarbonPath)]
        private static extern nint TISGetInputSourceProperty(nint source, nint key);

        [DllImport(CarbonPath)]
        private static extern nint TISCopyCurrentKeyboardInputSource();

        [DllImport(CarbonPath)]
        private static extern int TISSelectInputSource(nint source);

        [DllImport(CoreFoundationPath)]
        private static extern nint CFArrayGetCount(nint array);

        [DllImport(CoreFoundationPath)]
        private static extern nint CFArrayGetValueAtIndex(nint array, nint index);

        [DllImport(CoreFoundationPath)]
        private static extern nint CFStringGetLength(nint text);

        [DllImport(CoreFoundationPath, CharSet = CharSet.Unicode)]
        private static extern void CFStringGetCharacters(nint text, CFRange range, [Out] char[] buffer);

        [DllImport(CoreFoundationPath)]
        private static extern void CFRelease(nint reference);

        [StructLayout(LayoutKind.Sequential)]
        private struct CFRange
        {
            public nint Location;
            public nint Length;
        }
    }
}
