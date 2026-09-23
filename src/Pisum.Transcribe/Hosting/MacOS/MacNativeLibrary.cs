using Microsoft.Extensions.Logging;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// Checks once, when it is created at startup, that the Swift helper <c>libPisumMac.dylib</c> has the ABI this build
/// expects. A missing or stale helper is logged as an error, and its functions are not called after that.
/// </summary>
internal sealed class MacNativeLibrary
{
    /// <summary>
    /// The ABI version this build expects, the value of <c>pisum_abi_version</c> in <c>Abi.swift</c>.
    /// </summary>
    public const int ExpectedAbiVersion = 1;

    /// <summary>
    /// Initializes a new instance with the helper next to the executable.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public MacNativeLibrary(ILogger<MacNativeLibrary> logger)
        : this(PisumMac.AbiVersion, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance, for tests.
    /// </summary>
    /// <param name="readAbiVersion">Reads the helper's ABI version.</param>
    /// <param name="logger">The logger.</param>
    public MacNativeLibrary(Func<int> readAbiVersion, ILogger<MacNativeLibrary> logger)
    {
        int version;
        try
        {
            version = readAbiVersion();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            logger.LogError(exception, "The helper libPisumMac could not be loaded. Its functions are not called.");
            return;
        }

        if (version != ExpectedAbiVersion)
        {
            logger.LogError(
                "The helper libPisumMac has ABI version {ActualVersion}, but this build expects {ExpectedVersion}. " +
                "Its functions are not called.", version, ExpectedAbiVersion);
            return;
        }

        IsAvailable = true;
    }

    /// <summary>
    /// Whether the helper's functions may be called.
    /// </summary>
    public bool IsAvailable { get; }
}
