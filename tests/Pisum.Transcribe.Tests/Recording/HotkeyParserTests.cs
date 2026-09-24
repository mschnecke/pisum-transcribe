using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Settings;
using SharpHook.Data;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Pisum.Transcribe.Tests.Recording;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class HotkeyParserTests
{
    private readonly CapturingLogger<HotkeyParserTests> _logger = new();

    [Fact]
    public void Parse_DefaultSettings_ReturnsPlatformDefaultWithoutWarning()
    {
        // Act
        var hotkey = HotkeyParser.Parse(new RecordingSettings().Hotkey, _logger);

        // Assert
        hotkey.ShouldBe([Enum.Parse<KeyCode>(HotkeyParser.DefaultKeyName)], ignoreOrder: true);
        _logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_TwoKnownNames_ReturnsBothKeys()
    {
        // Act
        var hotkey = HotkeyParser.Parse(["VcLeftControl", "VcLeftMeta"], _logger);

        // Assert
        hotkey.ShouldBe([KeyCode.VcLeftControl, KeyCode.VcLeftMeta], ignoreOrder: true);
        _logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_CamelCaseName_ReturnsKey()
    {
        // Act
        var hotkey = HotkeyParser.Parse(["vcRightControl"], _logger);

        // Assert
        hotkey.ShouldBe([KeyCode.VcRightControl], ignoreOrder: true);
        _logger.Entries.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("NoSuchKey")]
    [InlineData("5")]
    [InlineData("VcA, VcB")]
    [InlineData("VcUndefined")]
    public void Parse_UnknownName_ReturnsDefaultAndLogsWarning(string name)
    {
        // Act
        var hotkey = HotkeyParser.Parse(["VcLeftControl", name], _logger);

        // Assert
        hotkey.ShouldBe([Enum.Parse<KeyCode>(HotkeyParser.DefaultKeyName)], ignoreOrder: true);
        _logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public void Parse_EmptyList_ReturnsDefaultAndLogsWarning()
    {
        // Act
        var hotkey = HotkeyParser.Parse([], _logger);

        // Assert
        hotkey.ShouldBe([Enum.Parse<KeyCode>(HotkeyParser.DefaultKeyName)], ignoreOrder: true);
        _logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public void Parse_Null_ReturnsDefaultAndLogsWarning()
    {
        // Act
        var hotkey = HotkeyParser.Parse(null, _logger);

        // Assert
        hotkey.ShouldBe([Enum.Parse<KeyCode>(HotkeyParser.DefaultKeyName)], ignoreOrder: true);
        _logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
    }
}
