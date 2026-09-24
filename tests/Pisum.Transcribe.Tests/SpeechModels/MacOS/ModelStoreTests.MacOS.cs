using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.SpeechModels;

namespace Pisum.Transcribe.Tests.SpeechModels;

// The free space of macOS, which counts purgeable space (design D9 of add-macos-setup).
public sealed partial class ModelStoreTests
{
    private const long Megabyte = 1024 * 1024;

    [Fact]
    public async Task InstallAsync_MacOSPurgeableSpaceCoversModel_StartsDownload()
    {
        // Arrange: 800 MB unused, and 5 GB the system can make available.
        var model = CreateModel([1]) with {SizeBytes = 1_144_290_016};
        var freeSpace = new MacFreeSpace(_ => 5L * 1024 * Megabyte, _ => 800 * Megabyte,
            NullLogger<MacFreeSpace>.Instance);
        var sut = new ModelStore(_paths, _httpClientFactory, _lifetime, _settingsStore, NullLogger<ModelStore>.Instance,
            freeSpace.GetAvailableFreeSpace);
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        // Act
        var install = sut.InstallAsync(model, _progress, TestContext.Current.CancellationToken);

        // Assert: the request was sent, so the disk space check passed.
        await Should.ThrowAsync<HttpRequestException>(install);
        _handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public void GetAvailableFreeSpace_CapacityCannotBeRead_FallsBackToVolumeFreeSpaceAndLogsWarning()
    {
        // Arrange
        var logger = new CapturingLogger<MacFreeSpace>();
        var sut = new MacFreeSpace(_ => throw new IOException("No capacity"), _ => 800 * Megabyte, logger);

        // Act
        var bytes = sut.GetAvailableFreeSpace(_root.Path);

        // Assert
        bytes.ShouldBe(800 * Megabyte);
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Exception is IOException);
    }
}
