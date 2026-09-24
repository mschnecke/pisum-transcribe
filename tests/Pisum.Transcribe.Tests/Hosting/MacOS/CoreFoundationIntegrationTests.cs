using System.Diagnostics;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class CoreFoundationIntegrationTests : IDisposable
{
    private readonly TempDirectory _folder = new();

    public CoreFoundationIntegrationTests()
    {
        Directory.CreateDirectory(_folder.Path);
    }

    public void Dispose()
    {
        _folder.Dispose();
    }

    [Fact]
    public void GetAvailableCapacityForImportantUsage_TempFolder_IsAtLeastTheFreeSpaceOfDriveInfo()
    {
        // Arrange
        var driveInfoBytes = new DriveInfo(_folder.Path).AvailableFreeSpace;

        // Act
        var bytes = CoreFoundation.GetAvailableCapacityForImportantUsage(_folder.Path);

        // Assert: with a margin for files that other processes write in between.
        bytes.ShouldBeGreaterThanOrEqualTo(driveInfoBytes - 100L * 1024 * 1024);
    }

    [Fact]
    public void ExcludeFromBackup_TempFolder_TmutilReportsItExcluded()
    {
        // Act
        CoreFoundation.ExcludeFromBackup(_folder.Path);

        // Assert
        RunTmutilIsExcluded(_folder.Path).ShouldStartWith("[Excluded]");
    }

    [Fact]
    public void ExcludeFromBackup_MissingFolder_ThrowsIOException()
    {
        // Act
        var exclude = () => CoreFoundation.ExcludeFromBackup(Path.Combine(_folder.Path, "missing"));

        // Assert
        exclude.ShouldThrow<IOException>();
    }

    [Fact]
    public void IsProcessTrusted_TestHost_ReturnsWithoutThrowing()
    {
        // Act
        var trusted = () => CoreFoundation.IsProcessTrusted();

        // Assert: the grant belongs to the terminal, so either answer is right.
        trusted.ShouldNotThrow();
    }

    private static string RunTmutilIsExcluded(string path)
    {
        using var process = Process.Start(new ProcessStartInfo("/usr/bin/tmutil", ["isexcluded", path])
        {
            RedirectStandardOutput = true,
        })!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output.Trim();
    }
}
