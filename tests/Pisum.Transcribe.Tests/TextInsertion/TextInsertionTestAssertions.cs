using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.TextInsertion;

internal static class TextInsertionTestAssertions
{
    /// <summary>
    /// Fails with a clear message when Windows refused to bring the test window to the front.
    /// </summary>
    public static void ShouldBeForeground(TestWindow window)
    {
        var tracker = new ForegroundWindowTracker(NullLogger<ForegroundWindowTracker>.Instance);
        tracker.IsForeground(new InsertionTarget(window.Handle, Environment.ProcessId, false)).ShouldBeTrue(
            "Windows did not bring the test window to the foreground. Run the test from an interactive session, " +
            "leave the keyboard and mouse alone, and try again.");
    }
}
