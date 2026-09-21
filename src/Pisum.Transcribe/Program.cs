using System.Reflection;
using Pisum.Transcribe.Hosting;
using Serilog;

namespace Pisum.Transcribe;

/// <summary>
/// The entry point. It replaces the <c>Main</c> that WPF generates from <c>App.xaml</c>,
/// which runs too late for the single-instance guard.
/// </summary>
internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\Pisum.Transcribe.SingleInstance";

    /// <summary>
    /// Runs the application on the STA UI thread.
    /// </summary>
    /// <returns>The process exit code.</returns>
    [STAThread]
    public static int Main()
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

            var app = new App(paths);
            app.InitializeComponent();
            return app.Run();
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
