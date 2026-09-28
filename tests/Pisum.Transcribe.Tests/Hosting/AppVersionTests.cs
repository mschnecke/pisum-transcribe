using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class AppVersionTests
{
    [Theory]
    [InlineData("1.5.0+fad2d9c", "1.5.0")]
    [InlineData("1.4.0-rc.1+abc", "1.4.0-rc.1")]
    [InlineData("1.5.0", "1.5.0")]
    [InlineData(null, null)]
    public void WithoutBuildMetadata_Version_RemovesTextFromThePlus(string? version, string? expected)
    {
        // Act
        var result = AppVersion.WithoutBuildMetadata(version);

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void Current_AppAssembly_IsTheVersionWithoutBuildMetadata()
    {
        // Act
        var result = AppVersion.Current;

        // Assert
        result.ShouldNotBeNullOrEmpty();
        result.ShouldNotContain('+');
    }
}
