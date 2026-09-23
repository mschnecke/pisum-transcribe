using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

/// <summary>
/// Starts the dev app bundle's executable, which shows the menu bar icon, so it needs the desktop. It runs with a HOME of
/// its own, which moves only its logs: macOS resolves Application Support without HOME, so it reads the user's settings
/// and models, and without a model it opens the setup window. It skips while Pisum Transcribe runs, whose
/// single-instance mutex would end it at once.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Hardware)]
public sealed partial class AppBundleHardwareTests : IDisposable
{
    private const int Sigterm = 15;

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExitBudget = TimeSpan.FromSeconds(5);

    private readonly TempDirectory _home = new();

    public void Dispose()
    {
        _home.Dispose();
    }

    [Fact(Explicit = true)]
    public void Sigterm_RunningApp_EndsWithTerminationRequestAndExitCode0Within5Seconds()
    {
        // Arrange
        var executable = FindBundleExecutable();
        Assert.SkipWhen(executable is null, "The dev app bundle wasn't built. Build the app on the Mac first.");
        Assert.SkipWhen(IsAppRunning(), "Pisum Transcribe is running. Quit it first.");
        using var app = Start(executable!);
        try
        {
            WaitForLog("Application started", StartTimeout, app);

            // Act
            kill(app.Id, Sigterm).ShouldBe(0);
            var exited = app.WaitForExit(ExitBudget);

            // Assert
            exited.ShouldBeTrue($"The app was still running after {ExitBudget}.");
            app.ExitCode.ShouldBe(0);
            ReadLog().ShouldContain("Shutting down, reason \"TerminationRequest\"");
        }
        finally
        {
            if (!app.HasExited)
            {
                app.Kill();
            }
        }
    }

    [LibraryImport("libc", SetLastError = true)]
    private static partial int kill(int pid, int signal);

    private static string? FindBundleExecutable()
    {
        // tests/Pisum.Transcribe.Tests/bin/<configuration>/net10.0/ → src/Pisum.Transcribe/bin/<configuration>/…
        var configuration = typeof(AppBundleHardwareTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Pisum.Transcribe.slnx")))
        {
            root = root.Parent;
        }

        var executable = root is null
            ? null
            : Path.Combine(root.FullName, "src", "Pisum.Transcribe", "bin", configuration, "net10.0", "osx-arm64",
                "Pisum Transcribe.app", "Contents", "MacOS", "Pisum.Transcribe");
        return File.Exists(executable) ? executable : null;
    }

    private static bool IsAppRunning()
    {
        // The app's own mutex, released at once.
        using var guard = new SingleInstanceGuard("Pisum.Transcribe.SingleInstance",
            new NamedWaitHandleOptions {CurrentUserOnly = true, CurrentSessionOnly = false});
        return !guard.TryAcquire(TimeSpan.Zero);
    }

    private Process Start(string executable)
    {
        Directory.CreateDirectory(_home.Path);
        var startInfo = new ProcessStartInfo(executable) {UseShellExecute = false};
        startInfo.Environment["HOME"] = _home.Path;
        return Process.Start(startInfo)!;
    }

    private string ReadLog()
    {
        var logs = Path.Combine(_home.Path, "Library", "Logs", "Pisum Transcribe");
        if (!Directory.Exists(logs))
        {
            return string.Empty;
        }

        // Serilog keeps the file open, so it is read with sharing.
        return string.Concat(Directory.GetFiles(logs).Select(file =>
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }));
    }

    private void WaitForLog(string text, TimeSpan timeout, Process app)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!ReadLog().Contains(text) && !app.HasExited && stopwatch.Elapsed < timeout)
        {
            Thread.Sleep(100);
        }

        ReadLog().ShouldContain(text);
    }
}
