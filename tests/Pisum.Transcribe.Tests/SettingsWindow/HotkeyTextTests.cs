using Pisum.Transcribe.SettingsWindow;
using SharpHook.Data;

namespace Pisum.Transcribe.Tests.SettingsWindow;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class HotkeyTextTests
{
    [Fact]
    public void Format_ModifiersAndOtherKeysOnWindows_ListsModifiersInCtrlAltShiftWinOrder()
    {
        // Act
        var text = HotkeyText.Format([KeyCode.VcA, KeyCode.VcRightMeta, KeyCode.VcLeftShift, KeyCode.VcRightAlt,
            KeyCode.VcLeftControl], HotkeyKeyNames.Windows);

        // Assert
        text.ShouldBe("Left Ctrl+Right Alt+Left Shift+Right Win+A");
    }

    [Fact]
    public void Format_ModifiersAndOtherKeysOnMacOS_ListsModifiersInFnControlOptionShiftCommandOrder()
    {
        // Act
        var text = HotkeyText.Format([KeyCode.VcA, KeyCode.VcRightMeta, KeyCode.VcLeftShift, KeyCode.VcRightAlt,
            KeyCode.VcLeftControl, KeyCode.VcFunction], HotkeyKeyNames.MacOS);

        // Assert
        text.ShouldBe("fn+Left Control+Right Option+Left Shift+Right Command+A");
    }

    [Theory]
    [InlineData(KeyCode.VcRightMeta, "Right Command")]
    [InlineData(KeyCode.VcLeftMeta, "Left Command")]
    [InlineData(KeyCode.VcLeftAlt, "Left Option")]
    [InlineData(KeyCode.VcRightControl, "Right Control")]
    [InlineData(KeyCode.VcFunction, "fn")]
    [InlineData(KeyCode.VcF13, "F13")]
    public void Format_SingleKeyOnMacOS_UsesMacName(KeyCode key, string expected)
    {
        // Act
        var text = HotkeyText.Format([key], HotkeyKeyNames.MacOS);

        // Assert
        text.ShouldBe(expected);
    }

    [Fact]
    public void Order_MacOS_SortsFnFirst()
    {
        // Act
        var keys = HotkeyText.Order([KeyCode.VcRightMeta, KeyCode.VcFunction], HotkeyKeyNames.MacOS);

        // Assert
        keys.ShouldBe([KeyCode.VcFunction, KeyCode.VcRightMeta]);
    }
}
