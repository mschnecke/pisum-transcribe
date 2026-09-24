using System.Diagnostics;

namespace Pisum.Transcribe.Tests.Hosting;

/// <summary>
/// Reads the power assertions that macOS lists, which include the user-initiated activities of every process.
/// </summary>
internal static class Pmset
{
    /// <summary>
    /// Runs <c>pmset -g assertions</c>.
    /// </summary>
    /// <returns>Its output.</returns>
    public static string Assertions()
    {
        using var process = Process.Start(new ProcessStartInfo("/usr/bin/pmset", "-g assertions")
        {
            RedirectStandardOutput = true,
        })!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }
}
