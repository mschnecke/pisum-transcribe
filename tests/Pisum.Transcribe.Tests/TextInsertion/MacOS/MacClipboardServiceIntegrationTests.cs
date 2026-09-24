using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.TextInsertion;

/// <summary>
/// <see cref="MacClipboardService"/> on the real helper, with a pasteboard of the tests' own.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class MacClipboardServiceIntegrationTests : IDisposable
{
    private readonly NamedPasteboard _pasteboard = new();
    private readonly MacClipboardService _sut;

    public MacClipboardServiceIntegrationTests()
    {
        _sut = new MacClipboardService(new MacPasteboard(_pasteboard.Name), true,
            NullLogger<MacClipboardService>.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
        _pasteboard.Dispose();
    }

    [Fact]
    public async Task TryRestoreAsync_AfterExcludedTranscript_PutsBackTheUsersContent()
    {
        // Arrange
        await _sut.TrySetTextAsync("invoice 4711", false);
        var snapshot = (await _sut.TrySnapshotAsync()).ShouldNotBeNull();
        await _sut.TrySetTextAsync("Grüße aus Köln – 5 €", true);
        var afterSet = _sut.SequenceNumber;

        // Act
        var restored = await _sut.TryRestoreAsync(snapshot);

        // Assert
        restored.ShouldBeTrue();
        _sut.SequenceNumber.ShouldBeGreaterThan(afterSet);
        var item = _pasteboard.Read().ShouldHaveSingleItem();
        item.Single(entry => entry.Type == NamedPasteboard.TextType).Data.ShouldBe("invoice 4711"u8.ToArray());
    }

    [Fact]
    public async Task TrySnapshotAsync_PasswordManagersConcealedItem_IsSensitive()
    {
        // Arrange
        _pasteboard.Write([NamedPasteboard.Text("hunter2"), NamedPasteboard.Marker(MacClipboardService.ConcealedType)]);

        // Act
        var snapshot = await _sut.TrySnapshotAsync();

        // Assert
        snapshot.ShouldNotBeNull().IsSensitive.ShouldBeTrue();
    }

    [Fact]
    public async Task TrySnapshotAsync_ContentOfAnEarlierRestore_IsNotSensitive()
    {
        // Arrange
        await _sut.TrySetTextAsync("invoice 4711", false);
        var snapshot = (await _sut.TrySnapshotAsync()).ShouldNotBeNull();
        await _sut.TrySetTextAsync("transcript", true);
        await _sut.TryRestoreAsync(snapshot);

        // Act
        var next = await _sut.TrySnapshotAsync();

        // Assert
        next.ShouldNotBeNull().IsSensitive.ShouldBeFalse();
    }

    [Fact]
    public async Task TrySnapshotAsync_ExcludedTranscript_IsSensitive()
    {
        // Arrange
        await _sut.TrySetTextAsync("transcript", true);

        // Act
        var snapshot = await _sut.TrySnapshotAsync();

        // Assert
        snapshot.ShouldNotBeNull().IsSensitive.ShouldBeTrue();
    }
}
