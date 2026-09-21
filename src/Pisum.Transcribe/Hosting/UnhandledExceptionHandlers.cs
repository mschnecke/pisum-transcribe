using Serilog;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// Process-wide handlers for exceptions that nothing else catches.
/// Exceptions on the UI thread are handled by <see cref="ShutdownCoordinator"/>.
/// </summary>
internal static class UnhandledExceptionHandlers
{
    /// <summary>
    /// Hooks <see cref="AppDomain.UnhandledException"/> and <see cref="TaskScheduler.UnobservedTaskException"/>.
    /// </summary>
    public static void Register()
    {
        // Log.Logger is read when the event fires, because the host replaces the bootstrap logger.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => OnUnhandledException(Log.Logger, e);
        TaskScheduler.UnobservedTaskException += (_, e) => OnUnobservedTaskException(Log.Logger, e);
    }

    /// <summary>
    /// Logs an exception that ends the process and flushes the log before the process dies.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="e">The event data.</param>
    internal static void OnUnhandledException(ILogger logger, UnhandledExceptionEventArgs e)
    {
        logger.Fatal(e.ExceptionObject as Exception, "Unhandled exception, the process ends");
        Log.CloseAndFlush();
    }

    /// <summary>
    /// Logs an exception of a task that nothing observed. The task has already ended, so the application keeps running.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="e">The event data.</param>
    internal static void OnUnobservedTaskException(ILogger logger, UnobservedTaskExceptionEventArgs e)
    {
        logger.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
}
