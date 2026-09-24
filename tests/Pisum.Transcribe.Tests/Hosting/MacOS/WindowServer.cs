using System.Diagnostics;
using System.Text.Json;
using Avalonia;

namespace Pisum.Transcribe.Tests.Hosting;

/// <summary>
/// What the window server reports about on-screen windows and screens, read through JavaScript for Automation, as the
/// placement spike of add-macos-dictation did.
/// </summary>
internal static class WindowServer
{
    private const string WindowsScript = """
        ObjC.import('CoreGraphics');
        function run(argv) {
          const list = ObjC.deepUnwrap(ObjC.castRefToObject($.CGWindowListCopyWindowInfo($.kCGWindowListOptionOnScreenOnly, 0)));
          return JSON.stringify(list.filter(w => w.kCGWindowOwnerPID === parseInt(argv[0], 10)).map(w => ({
            layer: w.kCGWindowLayer, x: w.kCGWindowBounds.X, y: w.kCGWindowBounds.Y,
            width: w.kCGWindowBounds.Width, height: w.kCGWindowBounds.Height })));
        }
        """;

    private const string ScreensScript = """
        ObjC.import('AppKit');
        function run() {
          const screens = $.NSScreen.screens;
          const primaryHeight = screens.objectAtIndex(0).frame.size.height;
          const result = [];
          for (let i = 0; i < screens.count; i++) {
            const v = screens.objectAtIndex(i).visibleFrame;
            result.push({ x: v.origin.x, y: primaryHeight - v.origin.y - v.size.height, width: v.size.width, height: v.size.height });
          }
          return JSON.stringify(result);
        }
        """;

    /// <summary>
    /// The on-screen windows of a process, with their layer: 0 for normal windows, 3 for the floating level.
    /// </summary>
    /// <param name="processId">The process.</param>
    /// <returns>The windows' bounds in global points, top-left origin.</returns>
    public static IReadOnlyList<(int Layer, Rect Bounds)> Windows(int processId)
    {
        using var document = JsonDocument.Parse(Run(WindowsScript, processId.ToString()));
        return document.RootElement.EnumerateArray()
            .Select(window => (window.GetProperty("layer").GetInt32(), new Rect(window.GetProperty("x").GetDouble(),
                window.GetProperty("y").GetDouble(), window.GetProperty("width").GetDouble(),
                window.GetProperty("height").GetDouble())))
            .ToList();
    }

    /// <summary>
    /// Reads the on-screen windows of a process until they satisfy a condition, because the window server lists a new
    /// or moved window a moment later.
    /// </summary>
    /// <param name="processId">The process.</param>
    /// <param name="condition">The condition.</param>
    /// <returns>The last windows read, which satisfy the condition unless 2 seconds passed.</returns>
    public static async Task<IReadOnlyList<(int Layer, Rect Bounds)>> WaitForWindowsAsync(int processId,
        Func<IReadOnlyList<(int Layer, Rect Bounds)>, bool> condition)
    {
        var started = Stopwatch.StartNew();
        var windows = Windows(processId);
        while (!condition(windows) && started.Elapsed < TimeSpan.FromSeconds(2))
        {
            await Task.Delay(50);
            windows = Windows(processId);
        }

        return windows;
    }

    /// <summary>
    /// The visible frame of each screen, without the menu bar and the Dock, flipped to global points with a top-left
    /// origin. The primary screen comes first.
    /// </summary>
    /// <returns>The visible frames.</returns>
    public static IReadOnlyList<Rect> VisibleFrames()
    {
        using var document = JsonDocument.Parse(Run(ScreensScript));
        return document.RootElement.EnumerateArray()
            .Select(frame => new Rect(frame.GetProperty("x").GetDouble(), frame.GetProperty("y").GetDouble(),
                frame.GetProperty("width").GetDouble(), frame.GetProperty("height").GetDouble()))
            .ToList();
    }

    private static string Run(string script, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/osascript") {RedirectStandardOutput = true};
        foreach (var argument in (string[]) ["-l", "JavaScript", "-e", script, .. arguments])
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }
}
