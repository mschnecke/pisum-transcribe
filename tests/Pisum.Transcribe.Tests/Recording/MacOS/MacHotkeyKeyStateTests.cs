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

    [Theory]
    [InlineData(0x36, 0x0010_0110UL, true)] // right Command held, as sampled on a MacBook Air
    [InlineData(0x36, 0x0010_0108UL, false)] // only left Command held
    [InlineData(0x37, 0x0010_0108UL, true)] // left Command held
    [InlineData(0x3D, 0x0008_0140UL, true)] // right Option held
    [InlineData(0x3A, 0x0008_0140UL, false)] // left Option isn't held
    [InlineData(0x3C, 0x0002_0104UL, true)] // right Shift held
    [InlineData(0x3E, 0x0004_2100UL, true)] // right Control held
    [InlineData(0x36, 0x0000_0100UL, false)] // nothing held
    public void IsKeyDown_ModifierKey_ReadsItsSideBitAndNotTheKeyState(int keyCode, ulong flags, bool expected)
    {
        // Arrange
        var keyStateReads = 0;

        // Act
        var isDown = MacHotkeyKeyState.IsKeyDown(keyCode, () => flags, _ =>
        {
            keyStateReads++;
            return true;
        });

        // Assert
        isDown.ShouldBe(expected);
        keyStateReads.ShouldBe(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsKeyDown_OtherKey_ReadsTheKeyState(bool keyState)
    {
        // Arrange: F13, with every modifier flag set.
        const int f13KeyCode = 0x69;

        // Act
        var isDown = MacHotkeyKeyState.IsKeyDown(f13KeyCode, () => ulong.MaxValue, key =>
        {
            key.ShouldBe(f13KeyCode);
            return keyState;
        });

        // Assert
        isDown.ShouldBe(keyState);
    }
}
