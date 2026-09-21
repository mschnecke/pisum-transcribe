using System.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.TextInsertion;

/// <summary>
/// Inserts text into a WPF window of the test process with real keystrokes and the real clipboard, whose contents the
/// tests replace. Needs an interactive desktop; leave the keyboard and mouse alone while it runs.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Hardware)]
[Collection(DesktopCollection.Name)]
public sealed class TextInserterHardwareTests : IDisposable
{
    private static readonly TimeSpan InputTimeout = TimeSpan.FromSeconds(5);

    private readonly WpfClipboardService _clipboard = new(NullLogger<WpfClipboardService>.Instance);
    private readonly SharpHookKeyboardInput _keyboard = new(NullLogger<SharpHookKeyboardInput>.Instance);
    private readonly ForegroundWindowTracker _tracker = new(NullLogger<ForegroundWindowTracker>.Instance);
    private readonly TextInserter _sut;

    public TextInserterHardwareTests()
    {
        _sut = new TextInserter(_clipboard, _keyboard, _tracker, TimeProvider.System, NullLogger<TextInserter>.Instance);
    }

    public void Dispose()
    {
        _keyboard.Dispose();
        _clipboard.Dispose();
    }

    [Fact(Explicit = true)]
    public async Task InsertAsync_PasteIntoFocusedTextBox_InsertsTextExactlyAndRestoresClipboard()
    {
        // Arrange
        const string transcript = "Grüße aus Köln – 5 €";
        using var window = TestWindow.Open();
        TextInsertionTestAssertions.ShouldBeForeground(window);
        RawClipboard.Set(new DataObject(DataFormats.UnicodeText, "invoice 4711"));
        var target = _tracker.CaptureForeground();

        // Act: the restore finishes after InsertAsync returns, and StopAsync waits for it.
        var outcome = await _sut.InsertAsync(transcript, target,
            new TextInsertionSettings(InsertionMethod.ClipboardPaste, true), TestContext.Current.CancellationToken);
        await _sut.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        outcome.ShouldBe(InsertionOutcome.Inserted);
        (await window.WaitForTextAsync(transcript, InputTimeout)).ShouldBe(transcript);
        RawClipboard.GetText().ShouldBe("invoice 4711");
    }

    [Fact(Explicit = true)]
    public async Task InsertAsync_TypeIntoFocusedTextBox_TypesLinesAndLeavesClipboardUnchanged()
    {
        // Arrange
        using var window = TestWindow.Open();
        TextInsertionTestAssertions.ShouldBeForeground(window);
        RawClipboard.Set(new DataObject(DataFormats.UnicodeText, "invoice 4711"));
        var target = _tracker.CaptureForeground();

        // Act
        var outcome = await _sut.InsertAsync("Hallo\nWelt 👋", target,
            new TextInsertionSettings(InsertionMethod.TypeText, true), TestContext.Current.CancellationToken);

        // Assert: a TextBox stores Enter as CR LF.
        outcome.ShouldBe(InsertionOutcome.Inserted);
        (await window.WaitForTextAsync("Hallo\r\nWelt 👋", InputTimeout)).ShouldBe("Hallo\r\nWelt 👋");
        RawClipboard.GetText().ShouldBe("invoice 4711");
    }
}
