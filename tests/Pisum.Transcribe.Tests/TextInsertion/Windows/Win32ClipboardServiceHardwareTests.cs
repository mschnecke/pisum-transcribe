using System.Diagnostics;
using System.Text;
using Windows.Win32;
using Windows.Win32.System.Ole;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.TextInsertion;

/// <summary>
/// Uses the real Windows clipboard, whose contents the tests replace. Needs an interactive desktop.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Hardware)]
[Collection(DesktopCollection.Name)]
public sealed class Win32ClipboardServiceHardwareTests : IDisposable
{
    private static readonly byte[] DwordZero = [0, 0, 0, 0];

    private readonly Win32ClipboardService _sut = new(NullLogger<Win32ClipboardService>.Instance);

    public void Dispose()
    {
        _sut.Dispose();
    }

    [Fact(Explicit = true)]
    public async Task TryRestoreAsync_SnapshotOfText_RoundTripsTextExactly()
    {
        // Arrange
        const string original = "invoice 4711 – Grüße aus Köln 👋\r\nsecond line ";
        RawClipboard.SetText(original);
        var snapshot = await _sut.TrySnapshotAsync();
        snapshot.ShouldNotBeNull();
        (await _sut.TrySetTextAsync("transcript", true)).ShouldBeTrue();
        RawClipboard.GetText().ShouldBe("transcript");

        // Act
        var restored = await _sut.TryRestoreAsync(snapshot);

        // Assert
        restored.ShouldBeTrue();
        RawClipboard.GetText().ShouldBe(original);
    }

    [Fact(Explicit = true)]
    public async Task TryRestoreAsync_SnapshotOfHtmlImageAndFileList_RoundTripsEachFormat()
    {
        // Arrange
        var html = Encoding.UTF8.GetBytes("<html><body><b>invoice 4711</b></body></html>\0");
        var image = Bitmap();
        var files = FileList(@"C:\invoices\4711.pdf");
        var htmlFormat = PInvoke.RegisterClipboardFormat("HTML Format");
        RawClipboard.Set([
            (htmlFormat, html),
            ((uint) CLIPBOARD_FORMAT.CF_DIB, image),
            ((uint) CLIPBOARD_FORMAT.CF_HDROP, files),
        ]);
        var snapshot = await _sut.TrySnapshotAsync();
        snapshot.ShouldNotBeNull();
        (await _sut.TrySetTextAsync("transcript", true)).ShouldBeTrue();

        // Act
        var restored = await _sut.TryRestoreAsync(snapshot);

        // Assert
        restored.ShouldBeTrue();
        RawClipboard.Read(htmlFormat).ShouldBe(html);
        RawClipboard.Read((uint) CLIPBOARD_FORMAT.CF_DIB).ShouldBe(image);
        RawClipboard.Read((uint) CLIPBOARD_FORMAT.CF_HDROP).ShouldBe(files);
    }

    [Fact(Explicit = true)]
    public async Task TrySetTextAsync_ExcludeFromHistory_SetsTextAndExclusionFormats()
    {
        // Act
        var set = await _sut.TrySetTextAsync("Grüße aus Köln – 5 €", true);

        // Assert
        set.ShouldBeTrue();
        RawClipboard.GetText().ShouldBe("Grüße aus Köln – 5 €");
        RawClipboard.Read(Win32ClipboardService.ExcludeFromMonitoringFormat).ShouldNotBeNull();
        RawClipboard.Read(Win32ClipboardService.HistoryFormat).ShouldBe(DwordZero);
        RawClipboard.Read(Win32ClipboardService.CloudFormat).ShouldBe(DwordZero);
        RawClipboard.Read(Win32ClipboardService.RestoredFormat).ShouldBeNull();
    }

    [Fact(Explicit = true)]
    public async Task TrySetTextAsync_NotExcludedFromHistory_SetsTextWithoutExclusionFormats()
    {
        // Act
        var set = await _sut.TrySetTextAsync("transcript", false);

        // Assert
        set.ShouldBeTrue();
        RawClipboard.GetText().ShouldBe("transcript");
        RawClipboard.Read(Win32ClipboardService.ExcludeFromMonitoringFormat).ShouldBeNull();
        RawClipboard.Read(Win32ClipboardService.HistoryFormat).ShouldBeNull();
        RawClipboard.Read(Win32ClipboardService.CloudFormat).ShouldBeNull();
    }

    [Fact(Explicit = true)]
    public async Task TryRestoreAsync_Snapshot_SetsHistoryFormatsAndRestoredMarkerWithoutMonitorExclusion()
    {
        // Arrange
        RawClipboard.SetText("invoice 4711");
        var snapshot = await _sut.TrySnapshotAsync();
        snapshot.ShouldNotBeNull();
        (await _sut.TrySetTextAsync("transcript", true)).ShouldBeTrue();

        // Act
        var restored = await _sut.TryRestoreAsync(snapshot);

        // Assert
        restored.ShouldBeTrue();
        RawClipboard.GetText().ShouldBe("invoice 4711");
        RawClipboard.Read(Win32ClipboardService.HistoryFormat).ShouldBe(DwordZero);
        RawClipboard.Read(Win32ClipboardService.CloudFormat).ShouldBe(DwordZero);
        RawClipboard.Read(Win32ClipboardService.RestoredFormat).ShouldNotBeNull();
        RawClipboard.Read(Win32ClipboardService.ExcludeFromMonitoringFormat).ShouldBeNull();
    }

    [Theory(Explicit = true)]
    [InlineData(Win32ClipboardService.ExcludeFromMonitoringFormat)]
    [InlineData(Win32ClipboardService.HistoryFormat)]
    [InlineData(Win32ClipboardService.CloudFormat)]
    public async Task TrySnapshotAsync_ContentWithOneExclusionFormat_ReportsSensitive(string format)
    {
        // Arrange
        RawClipboard.Set([
            ((uint) CLIPBOARD_FORMAT.CF_UNICODETEXT, Encoding.Unicode.GetBytes("password\0")),
            (PInvoke.RegisterClipboardFormat(format), DwordZero),
        ]);

        // Act
        var snapshot = await _sut.TrySnapshotAsync();

        // Assert
        snapshot.ShouldNotBeNull();
        snapshot.IsSensitive.ShouldBeTrue();
    }

    [Fact(Explicit = true)]
    public async Task TrySnapshotAsync_PlainText_ReportsNotSensitive()
    {
        // Arrange
        RawClipboard.SetText("invoice 4711");

        // Act
        var snapshot = await _sut.TrySnapshotAsync();

        // Assert
        snapshot.ShouldNotBeNull();
        snapshot.IsSensitive.ShouldBeFalse();
    }

    [Fact(Explicit = true)]
    public async Task TrySnapshotAsync_RestoredContent_ReportsNotSensitive()
    {
        // Arrange
        RawClipboard.SetText("invoice 4711");
        var first = await _sut.TrySnapshotAsync();
        first.ShouldNotBeNull();
        (await _sut.TrySetTextAsync("transcript", true)).ShouldBeTrue();
        (await _sut.TryRestoreAsync(first)).ShouldBeTrue();

        // Act
        var second = await _sut.TrySnapshotAsync();

        // Assert
        second.ShouldNotBeNull();
        second.IsSensitive.ShouldBeFalse();
        (await _sut.TrySetTextAsync("transcript", true)).ShouldBeTrue();
        (await _sut.TryRestoreAsync(second)).ShouldBeTrue();
        RawClipboard.GetText().ShouldBe("invoice 4711");
    }

    [Fact(Explicit = true)]
    public async Task TrySetTextAsync_ClipboardHeldOpenByAnotherThread_ReturnsFalseAfterAboutOneSecond()
    {
        // Arrange
        using var held = RawClipboard.Hold();
        var stopwatch = Stopwatch.StartNew();

        // Act
        var set = await _sut.TrySetTextAsync("transcript", false);

        // Assert
        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"Gave up after {stopwatch.ElapsedMilliseconds} ms");
        set.ShouldBeFalse();
        stopwatch.Elapsed.ShouldBeInRange(TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// A 1 × 1 pixel 32-bit bottom-up device-independent bitmap: a BITMAPINFOHEADER and one blue pixel.
    /// </summary>
    private static byte[] Bitmap()
    {
        var bytes = new byte[40 + 4];
        BitConverter.GetBytes(40).CopyTo(bytes, 0);
        BitConverter.GetBytes(1).CopyTo(bytes, 4);
        BitConverter.GetBytes(1).CopyTo(bytes, 8);
        BitConverter.GetBytes((short) 1).CopyTo(bytes, 12);
        BitConverter.GetBytes((short) 32).CopyTo(bytes, 14);
        BitConverter.GetBytes(4).CopyTo(bytes, 20);
        bytes[40] = 0xFF;
        bytes[43] = 0xFF;
        return bytes;
    }

    /// <summary>
    /// A CF_HDROP drop list: a DROPFILES header followed by the double-null-terminated Unicode paths.
    /// </summary>
    private static byte[] FileList(params string[] paths)
    {
        const int headerSize = 20;
        var names = Encoding.Unicode.GetBytes(string.Concat(paths.Select(path => path + '\0')) + '\0');
        var bytes = new byte[headerSize + names.Length];
        BitConverter.GetBytes(headerSize).CopyTo(bytes, 0);

        // fWide: the paths are Unicode.
        BitConverter.GetBytes(1).CopyTo(bytes, 16);
        names.CopyTo(bytes, headerSize);
        return bytes;
    }
}
