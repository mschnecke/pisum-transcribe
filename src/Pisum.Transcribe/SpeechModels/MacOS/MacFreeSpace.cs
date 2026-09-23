using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// The free space for a download on macOS: the capacity the system makes available for a download the user starts,
/// which includes purgeable space, as Finder shows it (design D9 of add-macos-setup).
/// </summary>
internal sealed class MacFreeSpace
{
    private readonly Func<string, long> _readImportantUsageCapacity;
    private readonly Func<string, long> _readDriveFreeSpace;
    private readonly ILogger<MacFreeSpace> _logger;

    /// <summary>
    /// Initializes a new instance that reads the capacity through CoreFoundation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public MacFreeSpace(ILogger<MacFreeSpace> logger)
        : this(CoreFoundation.GetAvailableCapacityForImportantUsage, path => new DriveInfo(path).AvailableFreeSpace,
            logger)
    {
    }

    /// <summary>
    /// Initializes a new instance, for tests.
    /// </summary>
    /// <param name="readImportantUsageCapacity">Reads the capacity for important usage of a folder's volume.</param>
    /// <param name="readDriveFreeSpace">Reads the free space of a folder's volume, the fallback.</param>
    /// <param name="logger">The logger.</param>
    public MacFreeSpace(Func<string, long> readImportantUsageCapacity,
                        Func<string, long> readDriveFreeSpace,
                        ILogger<MacFreeSpace> logger)
    {
        _readImportantUsageCapacity = readImportantUsageCapacity;
        _readDriveFreeSpace = readDriveFreeSpace;
        _logger = logger;
    }

    /// <summary>
    /// Returns the bytes available for a download into a folder, or the volume's free space when the capacity can't
    /// be read.
    /// </summary>
    /// <param name="folder">An existing folder.</param>
    /// <returns>The available bytes.</returns>
    public long GetAvailableFreeSpace(string folder)
    {
        try
        {
            return _readImportantUsageCapacity(folder);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Could not read the capacity for a download, using the volume's free space");
            return _readDriveFreeSpace(folder);
        }
    }
}
