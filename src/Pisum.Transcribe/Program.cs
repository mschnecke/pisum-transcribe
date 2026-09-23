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
    /// <summary>
    /// Runs the application on the STA UI thread.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The process exit code.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        using var singleInstanceGuard = CreateSingleInstanceGuard();
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

            // The platform's backend and Skia explicitly, instead of UsePlatformDetect, which would ship the other
            // platforms' backends too. The tray keeps the application running until ShutdownCoordinator ends it.
            var builder = AppBuilder.Configure(() => new App(paths));
#if WINDOWS
            builder = builder.UseWin32();
#else
            // No Dock icon when the app runs without its bundle. The bundle's LSUIElement does the same for a bundled
            // start.
            builder = builder.UseAvaloniaNative().With(new MacOSPlatformOptions {ShowInDock = false});
#endif
            return builder
                .UseSkia()
                .UseHarfBuzz()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static SingleInstanceGuard CreateSingleInstanceGuard()
    {
#if WINDOWS
        // One instance per Windows session.
        return new SingleInstanceGuard(@"Local\Pisum.Transcribe.SingleInstance");
#else
        // One instance per user. A Local\ name is scoped to the Unix session, and Finder and launchd start apps in a
        // session other than the terminal's.
        return new SingleInstanceGuard("Pisum.Transcribe.SingleInstance",
            new NamedWaitHandleOptions {CurrentUserOnly = true, CurrentSessionOnly = false});
#endif
    }
}
