using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.TextInsertion;

/// <summary>
/// Uses the real Windows clipboard, whose contents the tests replace. Needs an interactive desktop.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Hardware)]
[Collection(DesktopCollection.Name)]
public sealed class WpfClipboardServiceHardwareTests : IDisposable
{
    private static readonly byte[] DwordZero = [0, 0, 0, 0];

    private readonly WpfClipboardService _sut = new(NullLogger<WpfClipboardService>.Instance);

    public void Dispose()
    {
        _sut.Dispose();
    }

    [Fact(Explicit = true)]
    public async Task TryRestoreAsync_SnapshotOfText_RoundTripsTextExactly()
    {
        // Arrange
        const string original = "invoice 4711 – Grüße aus Köln 👋\r\nsecond line ";
        RawClipboard.Set(new DataObject(DataFormats.UnicodeText, original));
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
    public async Task TrySetTextAsync_ExcludeFromHistory_SetsTextAndExclusionFormats()
    {
        // Act
        var set = await _sut.TrySetTextAsync("Grüße aus Köln – 5 €", true);

        // Assert
        set.ShouldBeTrue();
        RawClipboard.GetText().ShouldBe("Grüße aus Köln – 5 €");
        RawClipboard.Read(WpfClipboardService.ExcludeFromMonitoringFormat).ShouldNotBeNull();
        RawClipboard.Read(WpfClipboardService.HistoryFormat).ShouldBe(DwordZero);
        RawClipboard.Read(WpfClipboardService.CloudFormat).ShouldBe(DwordZero);
        RawClipboard.Read(WpfClipboardService.RestoredFormat).ShouldBeNull();
    }

    [Fact(Explicit = true)]
    public async Task TrySetTextAsync_NotExcludedFromHistory_SetsTextWithoutExclusionFormats()
    {
        // Act
        var set = await _sut.TrySetTextAsync("transcript", false);

        // Assert
        set.ShouldBeTrue();
        RawClipboard.GetText().ShouldBe("transcript");
        RawClipboard.Read(WpfClipboardService.ExcludeFromMonitoringFormat).ShouldBeNull();
        RawClipboard.Read(WpfClipboardService.HistoryFormat).ShouldBeNull();
        RawClipboard.Read(WpfClipboardService.CloudFormat).ShouldBeNull();
    }

    [Fact(Explicit = true)]
    public async Task TryRestoreAsync_Snapshot_SetsHistoryFormatsAndRestoredMarkerWithoutMonitorExclusion()
    {
        // Arrange
        RawClipboard.Set(new DataObject(DataFormats.UnicodeText, "invoice 4711"));
        var snapshot = await _sut.TrySnapshotAsync();
        snapshot.ShouldNotBeNull();
        (await _sut.TrySetTextAsync("transcript", true)).ShouldBeTrue();

        // Act
        var restored = await _sut.TryRestoreAsync(snapshot);

        // Assert
        restored.ShouldBeTrue();
        RawClipboard.GetText().ShouldBe("invoice 4711");
        RawClipboard.Read(WpfClipboardService.HistoryFormat).ShouldBe(DwordZero);
        RawClipboard.Read(WpfClipboardService.CloudFormat).ShouldBe(DwordZero);
        RawClipboard.Read(WpfClipboardService.RestoredFormat).ShouldNotBeNull();
        RawClipboard.Read(WpfClipboardService.ExcludeFromMonitoringFormat).ShouldBeNull();
    }

    [Theory(Explicit = true)]
    [InlineData(WpfClipboardService.ExcludeFromMonitoringFormat)]
    [InlineData(WpfClipboardService.HistoryFormat)]
    [InlineData(WpfClipboardService.CloudFormat)]
    public async Task TrySnapshotAsync_ContentWithOneExclusionFormat_ReportsSensitive(string format)
    {
        // Arrange
        var data = new DataObject(DataFormats.UnicodeText, "password");
        data.SetData(format, new MemoryStream(DwordZero));
        RawClipboard.Set(data);

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
        RawClipboard.Set(new DataObject(DataFormats.UnicodeText, "invoice 4711"));

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
        RawClipboard.Set(new DataObject(DataFormats.UnicodeText, "invoice 4711"));
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
}
