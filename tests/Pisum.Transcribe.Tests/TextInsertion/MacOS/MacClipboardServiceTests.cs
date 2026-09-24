using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.TextInsertion;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class MacClipboardServiceTests : IDisposable
{
    private readonly IMacPasteboard _pasteboard = A.Fake<IMacPasteboard>();
    private readonly CapturingLogger<MacClipboardService> _logger = new();
    private readonly MacClipboardService _sut;

    public MacClipboardServiceTests()
    {
        A.CallTo(() => _pasteboard.AccessBehavior).Returns(2);
        A.CallTo(() => _pasteboard.SetText(A<string>._, A<bool>._)).Returns(true);
        A.CallTo(() => _pasteboard.Restore(A<byte[]>._)).Returns(true);
        _sut = new MacClipboardService(_pasteboard, true, _logger);
    }

    public void Dispose()
    {
        _sut.Dispose();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task TrySnapshotAsync_ReadNotAllowedWithoutAsking_ReturnsNullWithoutReading(int accessBehavior)
    {
        // Arrange
        A.CallTo(() => _pasteboard.AccessBehavior).Returns(accessBehavior);

        // Act
        var snapshot = await _sut.TrySnapshotAsync();

        // Assert
        snapshot.ShouldBeNull();
        A.CallTo(() => _pasteboard.Snapshot()).MustNotHaveHappened();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task TrySnapshotAsync_ReadAllowed_ReturnsTheItems(int accessBehavior)
    {
        // Arrange
        A.CallTo(() => _pasteboard.AccessBehavior).Returns(accessBehavior);
        Holds([Text("invoice 4711")]);

        // Act
        var snapshot = await _sut.TrySnapshotAsync();

        // Assert
        var mac = snapshot.ShouldBeOfType<MacClipboardSnapshot>();
        mac.IsSensitive.ShouldBeFalse();
        mac.Items.ShouldHaveSingleItem().ShouldHaveSingleItem().Type.ShouldBe("public.utf8-plain-text");
    }

    [Theory]
    [InlineData(MacClipboardService.ConcealedType)]
    [InlineData(MacClipboardService.TransientType)]
    public async Task TrySnapshotAsync_MarkedByTheSource_IsSensitive(string marker)
    {
        // Arrange
        Holds([Text("secret"), Marker(marker)]);

        // Act
        var snapshot = await _sut.TrySnapshotAsync();

        // Assert
        snapshot.ShouldNotBeNull().IsSensitive.ShouldBeTrue();
    }

    [Fact]
    public async Task TrySnapshotAsync_ContentOfAnEarlierRestore_IsNotSensitive()
    {
        // Arrange
        Holds(
            [Text("invoice 4711"), Marker(MacClipboardService.TransientType), Marker(MacClipboardService.RestoredType)],
            [Text("second")]);

        // Act
        var snapshot = await _sut.TrySnapshotAsync();

        // Assert
        snapshot.ShouldNotBeNull().IsSensitive.ShouldBeFalse();
    }

    [Fact]
    public async Task TrySnapshotAsync_SnapshotFails_ReturnsNull()
    {
        // Arrange
        A.CallTo(() => _pasteboard.Snapshot()).Returns(null);

        // Act
        var snapshot = await _sut.TrySnapshotAsync();

        // Assert
        snapshot.ShouldBeNull();
    }

    [Fact]
    public async Task TryRestoreAsync_Snapshot_WritesItsItems()
    {
        // Arrange
        Holds([Text("invoice 4711")], [Text("second")]);
        var snapshot = (await _sut.TrySnapshotAsync()).ShouldNotBeNull();
        byte[]? restored = null;
        A.CallTo(() => _pasteboard.Restore(A<byte[]>._)).Invokes((byte[] buffer) => restored = buffer).Returns(true);

        // Act
        var result = await _sut.TryRestoreAsync(snapshot);

        // Assert
        result.ShouldBeTrue();
        MacPasteboardFormat.Read(restored.ShouldNotBeNull()).ShouldNotBeNull().Count.ShouldBe(2);
    }

    [Fact]
    public async Task TrySetTextAsync_PasteboardRefuses_ReturnsFalse()
    {
        // Arrange
        A.CallTo(() => _pasteboard.SetText(A<string>._, A<bool>._)).Returns(false);

        // Act
        var result = await _sut.TrySetTextAsync("text", true);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task EveryCall_HelperUnavailable_FailsWithoutTouchingThePasteboard()
    {
        // Arrange
        using var sut = new MacClipboardService(_pasteboard, false, _logger);

        // Act
        var snapshot = await sut.TrySnapshotAsync();
        var set = await sut.TrySetTextAsync("text", false);
        var sequenceNumber = sut.SequenceNumber;

        // Assert
        snapshot.ShouldBeNull();
        set.ShouldBeFalse();
        sequenceNumber.ShouldBe(0);
        A.CallTo(_pasteboard).MustNotHaveHappened();
    }

    [Fact]
    public async Task TrySetTextAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        _sut.Dispose();

        // Act and Assert
        await Should.ThrowAsync<ObjectDisposedException>(() => _sut.TrySetTextAsync("text", false));
    }

    private static MacPasteboardEntry Text(string text)
    {
        return new MacPasteboardEntry("public.utf8-plain-text", System.Text.Encoding.UTF8.GetBytes(text));
    }

    private static MacPasteboardEntry Marker(string type)
    {
        return new MacPasteboardEntry(type, []);
    }

    private void Holds(params IReadOnlyList<MacPasteboardEntry>[] items)
    {
        A.CallTo(() => _pasteboard.Snapshot()).Returns(MacPasteboardFormat.Write(items));
    }
}
