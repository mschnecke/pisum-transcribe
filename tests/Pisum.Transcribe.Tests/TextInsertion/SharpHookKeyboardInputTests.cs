using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.TextInsertion;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class SharpHookKeyboardInputTests
{
    [Fact]
    public void SplitLines_MixedLineEndings_SplitsIntoLines()
    {
        // Act
        var lines = SharpHookKeyboardInput.SplitLines("a\r\nb\nc");

        // Assert
        lines.ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public void SplitLines_NoLineBreak_ReturnsOneLine()
    {
        // Act
        var lines = SharpHookKeyboardInput.SplitLines("Grüße aus Köln – 5 €");

        // Assert
        lines.ShouldBe(["Grüße aus Köln – 5 €"]);
    }
}
