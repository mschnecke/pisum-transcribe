using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.TextInsertion;

/// <summary>
/// The keystroke permission of the test host, which belongs to the terminal or IDE that runs the tests. The case that
/// matters, a grant made while the process runs, needs a revoke and a grant by hand (task 4.2 of add-macos-dictation).
/// </summary>
[Trait(Traits.Category, Traits.Categories.Hardware)]
public sealed class CoreGraphicsKeyEventsHardwareTests
{
    [Fact(Explicit = true)]
    public void CanPost_AccessibilityGrantedAtStart_ReturnsTrue()
    {
        // Arrange
        Assert.SkipWhen(!CoreFoundation.IsProcessTrusted(),
            "The terminal or IDE that runs the tests needs the Accessibility grant.");
        var sut = new CoreGraphicsKeyEvents();

        // Act
        var canPost = sut.CanPost();

        // Assert
        canPost.ShouldBeTrue();
    }
}
