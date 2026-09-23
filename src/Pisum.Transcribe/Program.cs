using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Pisum.Transcribe.Hosting;
using Serilog;

namespace Pisum.Transcribe;

/// <summary>
/// The entry point: the single-instance guard and the bootstrap logger, then the Avalonia application.
/// </summary>
internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\Pisum.Transcribe.SingleInstance";

    /// <summary>
    /// Runs the application on the STA UI thread.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The process exit code.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        using var singleInstanceGuard = new SingleInstanceGuard(SingleInstanceMutexName);
        if (!singleInstanceGuard.TryAcquire(SingleInstanceGuard.WaitTimeout))
        {
            // Another instance is running. Exit before the logger starts, because that instance holds today's log file open.
            return 0;
        }

        var paths = new AppPaths();
        paths.EnsureRootExists();
        Log.Logger = new LoggerConfiguration().WriteToAppLog(paths).CreateBootstrapLogger();
        UnhandledExceptionHandlers.Register();

        try
        {
            var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            Log.Information("Pisum Transcribe {Version} starting", version);

            // The Win32 backend and Skia explicitly, instead of UsePlatformDetect, which would ship the X11 and macOS
            // backends too. The tray keeps the application running until ShutdownCoordinator ends it.
            return AppBuilder.Configure(() => new App(paths))
                .UseWin32()
                .UseSkia()
                .UseHarfBuzz()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
