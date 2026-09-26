using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.TextInsertion;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class FrontmostApplicationTests
{
    private const int Self = 1;

    [Fact]
    public void Choose_NormalWindowFirst_ReturnsItsOwner()
    {
        // Act
        var owner = FrontmostApplication.Choose([(42, 0, 1.0), (43, 0, 1.0)], Self);

        // Assert
        owner.ShouldBe(42);
    }

    [Fact]
    public void Choose_OverlayMenuBarAndBannerInFront_SkipsOtherLayers()
    {
        // Act: the floating overlay (3), the menu bar (24) and a notification banner (23) are in front.
        var owner = FrontmostApplication.Choose([(50, 3, 1.0), (60, 24, 1.0), (70, 23, 1.0), (42, 0, 1.0)], Self);

        // Assert
        owner.ShouldBe(42);
    }

    [Fact]
    public void Choose_InvisibleWindowInFront_SkipsIt()
    {
        // Act
        var owner = FrontmostApplication.Choose([(50, 0, 0.0), (42, 0, 1.0)], Self);

        // Assert
        owner.ShouldBe(42);
    }

    [Fact]
    public void Choose_OwnWindowInFront_SkipsIt()
    {
        // Act
        var owner = FrontmostApplication.Choose([(Self, 0, 1.0), (42, 0, 1.0)], Self);

        // Assert
        owner.ShouldBe(42);
    }

    [Fact]
    public void Choose_NoNormalWindow_ReturnsNull()
    {
        // Act
        var owner = FrontmostApplication.Choose([(50, 3, 1.0), (Self, 0, 1.0)], Self);

        // Assert
        owner.ShouldBeNull();
    }
}
