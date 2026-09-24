using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.TextInsertion;

/// <summary>
/// Reads the current keyboard layout with the real Text Input Sources. A <c>Hardware</c> test, not an
/// <c>Integration</c> one: the app calls these functions on the main thread, which the test host can't reach, and
/// macOS may assert that and end the process (design D4 of add-macos-text-insertion). On macOS 27.0 the call works
/// from any thread, so run it on the development Mac only.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Hardware)]
public sealed class MacKeyboardLayoutHardwareTests
{
    [Fact(Explicit = true)]
    public void FindPasteKeyCode_LatinLayoutWithVOnTheUsKey_ReturnsUsKeyOfV()
    {
        // Arrange: QWERTY, QWERTZ and AZERTY all have V where a US keyboard has it.
        var sut = new MacKeyboardLayout();

        // Act
        var keyCode = sut.FindPasteKeyCode();

        // Assert
        keyCode.ShouldBe(MacKeyboardInput.DefaultPasteKeyCode);
    }
}
