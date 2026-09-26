using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class MacProcessActivityTests
{
    private readonly CapturingLogger<MacProcessActivity> _logger = new();

    [Fact]
    public void Begin_HelperUnavailable_DoesNothingAndLogsAWarningOnce()
    {
        // Arrange
        var library = new MacNativeLibrary(() => MacNativeLibrary.ExpectedAbiVersion + 1,
            new CapturingLogger<MacNativeLibrary>());
        var sut = new MacProcessActivity(library, _logger);

        // Act
        sut.Begin("first").Dispose();
        sut.Begin("second").Dispose();

        // Assert
        _logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
    }
}
