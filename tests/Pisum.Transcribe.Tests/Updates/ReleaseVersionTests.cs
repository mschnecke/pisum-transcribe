using Pisum.Transcribe.Updates;

namespace Pisum.Transcribe.Tests.Updates;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class ReleaseVersionTests
{
    [Fact]
    public void IsNewerThan_HigherRelease_IsNewer()
    {
        // Arrange
        var running = ReleaseVersion.ParseOwn("1.1.1+b917e0e").ShouldNotBeNull();
        var latest = ReleaseVersion.ParseTag("v1.2.0").ShouldNotBeNull();

        // Act
        var isNewer = latest.IsNewerThan(running);

        // Assert
        isNewer.ShouldBeTrue();
    }

    [Fact]
    public void IsNewerThan_SameVersion_IsNotNewer()
    {
        // Arrange
        var running = ReleaseVersion.ParseOwn("1.1.1").ShouldNotBeNull();
        var latest = ReleaseVersion.ParseTag("v1.1.1").ShouldNotBeNull();

        // Act
        var isNewer = latest.IsNewerThan(running);

        // Assert
        isNewer.ShouldBeFalse();
    }

    [Fact]
    public void IsNewerThan_OlderRelease_IsNotNewer()
    {
        // Arrange
        var running = ReleaseVersion.ParseOwn("1.3.0").ShouldNotBeNull();
        var latest = ReleaseVersion.ParseTag("v1.2.0").ShouldNotBeNull();

        // Act
        var isNewer = latest.IsNewerThan(running);

        // Assert
        isNewer.ShouldBeFalse();
    }

    [Fact]
    public void IsNewerThan_FinalReleaseAfterItsReleaseCandidate_IsNewer()
    {
        // Arrange
        var running = ReleaseVersion.ParseOwn("1.2.0-rc.1").ShouldNotBeNull();
        var latest = ReleaseVersion.ParseTag("v1.2.0").ShouldNotBeNull();

        // Act
        var isNewer = latest.IsNewerThan(running);

        // Assert
        isNewer.ShouldBeTrue();
    }

    [Fact]
    public void ParseTag_WithPreReleaseSuffix_Fails()
    {
        // Act
        var version = ReleaseVersion.ParseTag("v1.2.0-rc.1");

        // Assert
        version.ShouldBeNull();
    }

    [Fact]
    public void ParseOwn_WithBuildMetadata_IgnoresIt()
    {
        // Act
        var version = ReleaseVersion.ParseOwn("0.1.0-rc.2+4ac1cd3f");

        // Assert
        version.ShouldBe(new ReleaseVersion(0, 1, 0, true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.x.3")]
    [InlineData("-1.2.3")]
    [InlineData("v1.2.3")]
    public void ParseOwn_Invalid_Fails(string? text)
    {
        // Act
        var version = ReleaseVersion.ParseOwn(text);

        // Assert
        version.ShouldBeNull();
    }

    [Fact]
    public void ParseTag_Valid_ReturnsStableVersion()
    {
        // Act
        var version = ReleaseVersion.ParseTag("v1.12.0");

        // Assert
        version.ShouldBe(new ReleaseVersion(1, 12, 0, false));
        version?.ToString().ShouldBe("1.12.0");
    }

    [Fact]
    public void ParseTag_WithoutV_Fails()
    {
        // Act
        var version = ReleaseVersion.ParseTag("1.2.0");

        // Assert
        version.ShouldBeNull();
    }

    [Theory]
    [InlineData("v1.2.0.1")]
    [InlineData("v1.2.0.")]
    public void ParseTag_WithFourParts_Fails(string tag)
    {
        // Act
        var version = ReleaseVersion.ParseTag(tag);

        // Assert
        version.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1.2.0\n")]
    [InlineData(" v1.2.0")]
    [InlineData("v١.2.0")]
    [InlineData("v99999999999.0.0")]
    public void ParseTag_OtherForm_Fails(string? tag)
    {
        // Act
        var version = ReleaseVersion.ParseTag(tag);

        // Assert
        version.ShouldBeNull();
    }
}
