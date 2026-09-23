using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class MacNativeLibraryTests
{
    private readonly CapturingLogger<MacNativeLibrary> _logger = new();

    [Fact]
    public void Constructor_ExpectedVersion_IsAvailableWithoutLogging()
    {
        // Act
        var sut = new MacNativeLibrary(() => MacNativeLibrary.ExpectedAbiVersion, _logger);

        // Assert
        sut.IsAvailable.ShouldBeTrue();
        _logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_OtherVersion_LogsBothVersionsAndIsNotAvailable()
    {
        // Act
        var sut = new MacNativeLibrary(() => MacNativeLibrary.ExpectedAbiVersion + 1, _logger);

        // Assert
        sut.IsAvailable.ShouldBeFalse();
        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Properties.ShouldContain(new KeyValuePair<string, object?>("ActualVersion", MacNativeLibrary.ExpectedAbiVersion + 1));
        entry.Properties.ShouldContain(new KeyValuePair<string, object?>("ExpectedVersion", MacNativeLibrary.ExpectedAbiVersion));
    }

    [Fact]
    public void Constructor_LibraryMissing_LogsErrorAndIsNotAvailable()
    {
        // Act
        var sut = new MacNativeLibrary(() => throw new DllNotFoundException("libPisumMac"), _logger);

        // Assert
        sut.IsAvailable.ShouldBeFalse();
        _logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Error);
    }
}
