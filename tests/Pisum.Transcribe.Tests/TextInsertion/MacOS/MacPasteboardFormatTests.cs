using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.TextInsertion;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class MacPasteboardFormatTests
{
    [Fact]
    public void Read_EmptyPasteboard_ReturnsNoItems()
    {
        // Arrange
        var buffer = MacPasteboardFormat.Write([]);

        // Act
        var items = MacPasteboardFormat.Read(buffer);

        // Assert
        buffer.ShouldBe(new byte[] {0, 0, 0, 0});
        items.ShouldNotBeNull().ShouldBeEmpty();
    }

    [Fact]
    public void Read_WrittenItems_ReturnsTheSameItemsTypesAndData()
    {
        // Arrange
        IReadOnlyList<IReadOnlyList<MacPasteboardEntry>> written =
        [
            [
                new MacPasteboardEntry("public.utf8-plain-text", "Grüße 👋"u8.ToArray()),
                new MacPasteboardEntry("public.html", []),
            ],
            [new MacPasteboardEntry("public.file-url", "file:///tmp/a"u8.ToArray())],
        ];

        // Act
        var items = MacPasteboardFormat.Read(MacPasteboardFormat.Write(written)).ShouldNotBeNull();

        // Assert
        items.Count.ShouldBe(2);
        items[0].Select(entry => entry.Type).ShouldBe(["public.utf8-plain-text", "public.html"]);
        items[0][0].Data.ShouldBe("Grüße 👋"u8.ToArray());
        items[0][1].Data.ShouldBeEmpty();
        items[1].ShouldHaveSingleItem().Data.ShouldBe("file:///tmp/a"u8.ToArray());
    }

    [Fact]
    public void Write_OneItem_IsLittleEndianWithLengthPrefixes()
    {
        // Act
        var buffer = MacPasteboardFormat.Write([[new MacPasteboardEntry("t", [7])]]);

        // Assert
        buffer.ShouldBe(new byte[] {1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, (byte) 't', 1, 0, 0, 0, 0, 0, 0, 0, 7});
    }

    [Theory]
    [InlineData(new byte[] {})]
    [InlineData(new byte[] {1, 0, 0})]
    [InlineData(new byte[] {255, 255, 255, 255})]
    [InlineData(new byte[] {1, 0, 0, 0, 1, 0, 0, 0, 5, 0, 0, 0, (byte) 't'})]
    [InlineData(new byte[] {1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, (byte) 't', 9, 0, 0, 0, 0, 0, 0, 0, 7})]
    [InlineData(new byte[] {0, 0, 0, 0, 0})]
    public void Read_MalformedBuffer_ReturnsNull(byte[] buffer)
    {
        // Act
        var items = MacPasteboardFormat.Read(buffer);

        // Assert
        items.ShouldBeNull();
    }
}
