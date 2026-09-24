using Pisum.Transcribe.Recording;

namespace Pisum.Transcribe.Tests.Recording;

[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class MacHotkeyKeyStateIntegrationTests
{
    // kVK_F19, which no test run holds.
    private const int F19KeyCode = 0x50;

    [Fact]
    public void IsKeyDown_IdleKey_IsFalse()
    {
        // Act
        var isDown = MacHotkeyKeyState.IsKeyDown(F19KeyCode);

        // Assert
        isDown.ShouldBeFalse();
    }

    [Fact]
    public void IsKeyDown_IdleModifier_IsFalse()
    {
        // Act: right Option, which no test run holds.
        var isDown = MacHotkeyKeyState.IsKeyDown(0x3D);

        // Assert
        isDown.ShouldBeFalse();
    }

    [Fact]
    public void ReadSession_TestHost_IsOnConsoleAndNotLocked()
    {
        // Act
        var session = MacHotkeyKeyState.ReadSession();

        // Assert
        session.ShouldBe(new MacHotkeyKeyState.SessionState(true, false));
    }
}
