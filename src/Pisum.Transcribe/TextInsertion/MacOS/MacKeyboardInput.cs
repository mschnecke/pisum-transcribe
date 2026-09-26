using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// Keystrokes as CoreGraphics events, and the modifier keys from the HID system state (design D4 of
/// add-macos-text-insertion).
/// </summary>
/// <remarks>
/// <para>
/// Command+V is V down and up with the Command flag set on both events, and no Command key event, so it never mixes
/// with a right Command that the user still holds. The V is the key that produces "v" with Command in the current
/// layout, computed on the UI thread at start and after each input source change, and kept for the paste.
/// </para>
/// <para>
/// Typed text goes out in chunks of at most <see cref="ChunkLength"/> UTF-16 units, as much as one event carries, with
/// key code 0. Applications that translate the key code themselves, such as virtual machine guests, type "a" instead.
/// </para>
/// </remarks>
internal sealed class MacKeyboardInput : IKeyboardInput, IHostedService
{
    /// <summary>
    /// The most UTF-16 units one keyboard event carries.
    /// </summary>
    public const int ChunkLength = 20;

    /// <summary>
    /// The key of V on a US keyboard (<c>kVK_ANSI_V</c>), for layouts where no key produces "v", such as Russian, where
    /// macOS falls back to the Latin layout itself.
    /// </summary>
    public const ushort DefaultPasteKeyCode = 9;

    // kVK_Return
    private const ushort ReturnKeyCode = 36;

    // kCGEventFlagMaskShift, kCGEventFlagMaskControl, kCGEventFlagMaskAlternate and kCGEventFlagMaskCommand
    private const ulong ShiftFlag = 0x0002_0000;
    private const ulong ControlFlag = 0x0004_0000;
    private const ulong OptionFlag = 0x0008_0000;
    private const ulong CommandFlag = 0x0010_0000;

    private readonly IMacKeyEvents _events;
    private readonly IKeyboardLayout _layout;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger<MacKeyboardInput> _logger;
    private volatile int _pasteKeyCode = DefaultPasteKeyCode;
    private IDisposable? _layoutObserver;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="events">The keyboard events.</param>
    /// <param name="layout">The keyboard layout, read on the UI thread.</param>
    /// <param name="uiDispatcher">The UI thread.</param>
    /// <param name="logger">The logger.</param>
    public MacKeyboardInput(IMacKeyEvents events,
                            IKeyboardLayout layout,
                            IUiDispatcher uiDispatcher,
                            ILogger<MacKeyboardInput> logger)
    {
        _events = events;
        _layout = layout;
        _uiDispatcher = uiDispatcher;
        _logger = logger;
    }

    /// <summary>
    /// The key code the paste uses for V.
    /// </summary>
    public ushort PasteKeyCode => (ushort) _pasteKeyCode;

    /// <inheritdoc />
    public void SendPaste()
    {
        var keyCode = PasteKeyCode;
        _events.PostKey(keyCode, true, CommandFlag);
        _events.PostKey(keyCode, false, CommandFlag);
    }

    /// <inheritdoc />
    public void TypeText(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                _events.PostKey(ReturnKeyCode, true, 0);
                _events.PostKey(ReturnKeyCode, false, 0);
            }

            foreach (var chunk in SplitIntoChunks(lines[i]))
            {
                _events.PostText(chunk);
            }
        }
    }

    /// <inheritdoc />
    public bool CanPostEvents()
    {
        return _events.CanPost();
    }

    /// <inheritdoc />
    public bool AreModifiersDown(bool includePasteModifier)
    {
        var mask = ShiftFlag | ControlFlag | OptionFlag | (includePasteModifier ? CommandFlag : 0);
        return (_events.ReadFlags() & mask) != 0;
    }

    /// <summary>
    /// Reads the paste key and starts observing input source changes, on the UI thread.
    /// </summary>
    /// <param name="cancellationToken">Not used.</param>
    /// <returns>A task that completes when the UI thread has run it.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() =>
        {
            UpdatePasteKey();
            _layoutObserver = _layout.ObserveChanges(() => _ = _uiDispatcher.InvokeAsync(UpdatePasteKey));
        });
    }

    /// <summary>
    /// Stops observing input source changes, on the UI thread.
    /// </summary>
    /// <param name="cancellationToken">Not used.</param>
    /// <returns>A task that completes when the UI thread has run it.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return _uiDispatcher.InvokeAsync(() => _layoutObserver?.Dispose());
    }

    /// <summary>
    /// Splits text into chunks of at most <see cref="ChunkLength"/> UTF-16 units, never between the two halves of a
    /// surrogate pair.
    /// </summary>
    /// <param name="text">The text of one line.</param>
    /// <returns>The chunks, none for empty text.</returns>
    internal static IEnumerable<string> SplitIntoChunks(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(ChunkLength, text.Length - start);
            if (start + length < text.Length && char.IsHighSurrogate(text[start + length - 1]))
            {
                length--;
            }

            yield return text.Substring(start, length);
            start += length;
        }
    }

    private void UpdatePasteKey()
    {
        ushort? keyCode;
        try
        {
            keyCode = _layout.FindPasteKeyCode();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The keyboard layout could not be read, pasting with the US key of V");
            keyCode = null;
        }

        _pasteKeyCode = keyCode ?? DefaultPasteKeyCode;
        _logger.LogDebug("The paste uses key code {KeyCode}", _pasteKeyCode);
    }
}
