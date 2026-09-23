using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class MacNativeLibraryIntegrationTests
{
    [Fact]
    public void AbiVersion_RealHelper_IsTheExpectedVersion()
    {
        // Act
        var version = PisumMac.AbiVersion();

        // Assert
        version.ShouldBe(MacNativeLibrary.ExpectedAbiVersion);
    }
}
