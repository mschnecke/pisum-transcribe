using Serilog;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// The Serilog setup shared by the bootstrap logger and the host logger.
/// </summary>
internal static class LoggerConfigurationExtensions
{
    /// <summary>
    /// Writes to a daily rolling file under <see cref="AppPaths.LogsDirectory"/> and keeps the 7 most recent files.
    /// Logs never leave the machine, and they never contain transcript text or audio data.
    /// </summary>
    /// <param name="configuration">The configuration to extend.</param>
    /// <param name="paths">The application data folders.</param>
    /// <returns>The same configuration, for chaining.</returns>
    public static LoggerConfiguration WriteToAppLog(this LoggerConfiguration configuration, AppPaths paths)
    {
#if DEBUG
        configuration.MinimumLevel.Debug();
#else
        configuration.MinimumLevel.Information();
#endif
        return configuration.WriteTo.File(
            Path.Combine(paths.LogsDirectory, "pisum-transcribe-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7);
    }
}
