using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

/// <summary>
/// The parts of the relaunch that touch the system, on the dev app bundle (design D10 of add-macos-setup). The whole
/// relaunch after a real Accessibility grant is checked by hand, because a test can't grant it.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed partial class AppBundleIntegrationTests : IDisposable
{
    private const int Sigterm = 15;
    private const string ExecutableName = "Pisum.Transcribe";

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(15);

    private readonly TempDirectory _folder = new();

    public void Dispose()
    {
        _folder.Dispose();
    }

    [Fact]
    public void FindPath_DevBundleExecutableFolder_ReturnsTheBundle()
    {
        // Arrange
        var bundle = FindDevBundle();
        Assert.SkipWhen(bundle is null, "The dev app bundle wasn't built. Build the app on the Mac first.");

        // Act
        var path = AppBundle.FindPath(Path.Combine(bundle!, "Contents", "MacOS") + Path.DirectorySeparatorChar);

        // Assert
        path.ShouldBe(bundle);
        Path.GetFileName(path).ShouldBe("Pisum Transcribe.app");
    }

    [Fact]
    public void FindPath_FolderWithoutBundle_ReturnsNull()
    {
        // Arrange
        Directory.CreateDirectory(_folder.Path);

        // Act
        var path = AppBundle.FindPath(_folder.Path);

        // Assert
        path.ShouldBeNull();
    }

    [Fact]
    public void StartNewInstance_DevBundle_StartsANewProcessOfTheBundle()
    {
        // Arrange
        var bundle = FindDevBundle();
        Assert.SkipWhen(bundle is null, "The dev app bundle wasn't built. Build the app on the Mac first.");
        var executable = Path.Combine(bundle!, "Contents", "MacOS", ExecutableName);
        var runningBefore = FindProcessIds(executable);

        // Act
        AppBundle.StartNewInstance(bundle!);

        // Assert
        var stopwatch = Stopwatch.StartNew();
        int[] started;
        while ((started = FindProcessIds(executable).Except(runningBefore).ToArray()).Length == 0
               && stopwatch.Elapsed < StartTimeout)
        {
            Thread.Sleep(100);
        }

        try
        {
            started.ShouldHaveSingleItem();
        }
        finally
        {
            foreach (var pid in started)
            {
                kill(pid, Sigterm);
            }
        }
    }

    [LibraryImport("libc", SetLastError = true)]
    private static partial int kill(int pid, int signal);

    private static int[] FindProcessIds(string executable)
    {
        var ids = new List<int>();
        foreach (var process in Process.GetProcessesByName(ExecutableName))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName == executable)
                    {
                        ids.Add(process.Id);
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException
                                                      or System.ComponentModel.Win32Exception)
                {
                    // The process has ended meanwhile.
                }
            }
        }

        return ids.ToArray();
    }

    private static string? FindDevBundle()
    {
        // tests/Pisum.Transcribe.Tests/bin/<configuration>/net10.0/ → src/Pisum.Transcribe/bin/<configuration>/…
        var configuration = typeof(AppBundleIntegrationTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Pisum.Transcribe.slnx")))
        {
            root = root.Parent;
        }

        var bundle = root is null
            ? null
            : Path.Combine(root.FullName, "src", "Pisum.Transcribe", "bin", configuration, "net10.0", "osx-arm64",
                "Pisum Transcribe.app");
        return Directory.Exists(bundle) ? bundle : null;
    }
}
