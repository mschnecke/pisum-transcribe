using System.Diagnostics;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// The app bundle the application runs from, and the start of a new instance of it for the relaunch (design D4 of
/// add-macos-setup).
/// </summary>
internal static class AppBundle
{
    /// <summary>
    /// Finds the app bundle that encloses a folder, such as <see cref="AppContext.BaseDirectory"/>, which is the
    /// bundle's <c>Contents/MacOS</c> folder.
    /// </summary>
    /// <param name="directory">The folder.</param>
    /// <returns>The path of the enclosing <c>.app</c> folder, or <see langword="null"/> without one.</returns>
    public static string? FindPath(string directory)
    {
        for (var folder = new DirectoryInfo(directory); folder is not null; folder = folder.Parent)
        {
            if (folder.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                return folder.FullName;
            }
        }

        return null;
    }

    /// <summary>
    /// Starts a new instance of the app bundle through LaunchServices, also while an instance runs, and returns at once.
    /// </summary>
    /// <param name="bundlePath">The path of the <c>.app</c> folder.</param>
    public static void StartNewInstance(string bundlePath)
    {
        // -n, because without it open activates the running instance instead of starting a second one.
        using var process = Process.Start(new ProcessStartInfo("/usr/bin/open", ["-n", bundlePath]));
    }
}
