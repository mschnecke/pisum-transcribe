using System.Runtime.InteropServices;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// Ends the process at once on macOS.
/// </summary>
internal static class ProcessTermination
{
    /// <summary>
    /// Ends the process through libc's <c>_exit</c>, without running process-exit handlers.
    /// </summary>
    /// <param name="exitCode">The exit code.</param>
    public static void Exit(int exitCode)
    {
        // Not Environment.Exit: its process-exit handlers would take from the 5 s exit budget.
        _exit(exitCode);
    }

    [DllImport("libc")]
    private static extern void _exit(int status);
}
