using System.Diagnostics;
using Windows.Win32;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// Ends the process at once on Windows.
/// </summary>
internal static class ProcessTermination
{
    /// <summary>
    /// Ends the process through <c>TerminateProcess</c>, without running process-exit handlers.
    /// </summary>
    /// <param name="exitCode">The exit code.</param>
    public static void Exit(int exitCode)
    {
        // Not Environment.Exit: its process-exit handlers take about 330 ms, which would break the 5 s exit budget.
        using var process = Process.GetCurrentProcess();
        PInvoke.TerminateProcess(process.SafeHandle, (uint) exitCode);
    }
}
