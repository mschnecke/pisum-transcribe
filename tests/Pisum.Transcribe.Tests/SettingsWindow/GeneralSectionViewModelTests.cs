using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SettingsWindow;

namespace Pisum.Transcribe.Tests.SettingsWindow;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class GeneralSectionViewModelTests
{
    [Theory]
    [InlineData("1.5.0", "Version 1.5.0")]
    [InlineData("1.4.0-rc.1", "Version 1.4.0-rc.1")]
    [InlineData(null, null)]
    public void Constructor_Version_ShowsItAsVersionText(string? version, string? expected)
    {
        // Act
        var sut = new GeneralSectionViewModel(false, false, false, new UpdateSettings(), version);

        // Assert
        sut.VersionText.ShouldBe(expected);
    }
}
