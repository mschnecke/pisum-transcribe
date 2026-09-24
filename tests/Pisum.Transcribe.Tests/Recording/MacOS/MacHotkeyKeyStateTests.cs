using Pisum.Transcribe.Recording;

namespace Pisum.Transcribe.Tests.Recording;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class MacHotkeyKeyStateTests
{
    private const int RightCommandKeyCode = 0x36;

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void IsHeld_KeyDown_IsTrueOnlyOnConsoleAndUnlocked(bool isOnConsole, bool isScreenLocked, bool expected)
    {
        // Arrange
        var sut = new MacHotkeyKeyState(_ => true,
            () => new MacHotkeyKeyState.SessionState(isOnConsole, isScreenLocked));

        // Act
        var held = sut.IsHeld(RightCommandKeyCode);

        // Assert
        held.ShouldBe(expected);
    }

    [Fact]
    public void IsHeld_KeyUp_IsFalse()
    {
        // Arrange
        var sut = new MacHotkeyKeyState(_ => false, () => new MacHotkeyKeyState.SessionState(true, false));

        // Act
        var held = sut.IsHeld(RightCommandKeyCode);

        // Assert
        held.ShouldBeFalse();
    }

    [Fact]
    public void IsHeld_KeyCode_ReadsThatKey()
    {
        // Arrange
        var readKeyCodes = new List<int>();
        var sut = new MacHotkeyKeyState(keyCode =>
        {
            readKeyCodes.Add(keyCode);
            return true;
        }, () => new MacHotkeyKeyState.SessionState(true, false));

        // Act
        sut.IsHeld(RightCommandKeyCode);

        // Assert
        readKeyCodes.ShouldBe([RightCommandKeyCode]);
    }
}
