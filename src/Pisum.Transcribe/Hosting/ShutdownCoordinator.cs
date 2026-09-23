using Avalonia.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Notifications;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// The only code that ends the application, so that the Avalonia application and the host end together.
/// </summary>
/// <remarks>
/// Create it on the UI thread. The shutdown runs once: it starts a watchdog that ends the process after
/// <see cref="ExitTimeout"/>, removes the tray icon (at once for <see cref="ShutdownReason.UserExit"/> and
/// <see cref="ShutdownReason.SessionEnd"/>; for <see cref="ShutdownReason.Error"/> after the error notification and after
/// the host has stopped), stops and disposes the host, and shuts down the Avalonia lifetime. The error notification is a
/// Windows toast, which stays in the notification center after the process has ended.
/// </remarks>
internal sealed class ShutdownCoordinator
{
    /// <summary>
    /// The time after which the process ends, even if the host has not stopped. Below the 5 s exit budget, so the
    /// process has ended by then.
    /// </summary>
    public static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(4.5);

    private readonly IHost _host;
    private readonly ITrayIconService _trayIcon;
    private readonly INotifier _notifier;
    private readonly TimeProvider _timeProvider;
    private readonly Action<int> _shutdownApplication;
    private readonly Action<int> _exitProcess;
    private readonly ILogger<ShutdownCoordinator> _logger;
    private readonly SynchronizationContext? _uiContext;

    // Without RunContinuationsAsynchronously, like an async method's task: a synchronous continuation runs in the
    // same dispatcher operation that shuts down the Avalonia lifetime (see DispatcherWait).
    private readonly TaskCompletionSource _shutdown = new();

    private int _shutdownRequested;

    // Referenced, so the timer is not collected before it fires.
    private ITimer? _watchdog;

    /// <summary>
    /// Initializes a new instance and subscribes to <see cref="ITrayIconService.ExitRequested"/> and
    /// <see cref="IHostApplicationLifetime.ApplicationStopping"/>.
    /// </summary>
    /// <param name="host">The host to stop and dispose.</param>
    /// <param name="lifetime">The lifetime of <paramref name="host"/>.</param>
    /// <param name="trayIcon">The tray icon.</param>
    /// <param name="notifier">Shows the notification after an error.</param>
    /// <param name="timeProvider">The time provider for the watchdog.</param>
    /// <param name="shutdownApplication">Shuts down the Avalonia lifetime with an exit code.</param>
    /// <param name="exitProcess">Ends the process at once with an exit code.</param>
    /// <param name="logger">The logger.</param>
    public ShutdownCoordinator(IHost host,
                               IHostApplicationLifetime lifetime,
                               ITrayIconService trayIcon,
                               INotifier notifier,
                               TimeProvider timeProvider,
                               Action<int> shutdownApplication,
                               Action<int> exitProcess,
                               ILogger<ShutdownCoordinator> logger)
    {
        _host = host;
        _trayIcon = trayIcon;
        _notifier = notifier;
        _timeProvider = timeProvider;
        _shutdownApplication = shutdownApplication;
        _exitProcess = exitProcess;
        _logger = logger;
        _uiContext = SynchronizationContext.Current;

        trayIcon.ExitRequested += (_, _) => _ = RequestShutdownAsync(ShutdownReason.UserExit);
        lifetime.ApplicationStopping.Register(OnApplicationStopping);
    }

    /// <summary>
    /// Ends the application. Only the first call starts a shutdown, and later calls return its task. The reason of the
    /// first call decides the exit code and when the tray icon is removed.
    /// </summary>
    /// <param name="reason">Why the application ends.</param>
    /// <returns>A task that completes when the Avalonia lifetime was shut down.</returns>
    public Task RequestShutdownAsync(ShutdownReason reason)
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) == 0)
        {
            _ = ShutdownAsync(reason);
        }

        return _shutdown.Task;
    }

    /// <summary>
    /// Handles <see cref="Dispatcher.UnhandledException"/>: logs the exception and ends the application with an error.
    /// </summary>
    /// <param name="sender">The dispatcher.</param>
    /// <param name="e">The event data.</param>
    public void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger.LogError(e.Exception, "Unhandled exception on the UI thread");
        e.Handled = true;
        _ = RequestShutdownAsync(ShutdownReason.Error);
    }

    private async Task ShutdownAsync(ShutdownReason reason)
    {
        var exitCode = reason != ShutdownReason.Error ? 0 : 1;

        // Everything that can throw runs in the try, so the Avalonia lifetime always shuts down and the task always
        // completes. The lifetime's ShutdownRequested handler in App waits for it.
        try
        {
            _logger.LogInformation("Shutting down, reason {Reason}", reason);
            _watchdog = _timeProvider.CreateTimer(_ => OnWatchdogElapsed(exitCode), null, ExitTimeout,
                Timeout.InfiniteTimeSpan);

            if (reason != ShutdownReason.Error)
            {
                _trayIcon.Remove();
            }
            else
            {
                _notifier.Show(
                    "Pisum Transcribe stopped",
                    "Pisum Transcribe stopped because of an error. Details are in the log.");
            }

            // Awaited instead of blocking the UI thread, so services that marshal to it while they stop do not deadlock.
            await _host.StopAsync();

            if (reason == ShutdownReason.Error)
            {
                _trayIcon.Remove();
            }

            if (_host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                _host.Dispose();
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Shutdown did not complete cleanly");
        }
        finally
        {
            try
            {
                _shutdownApplication(exitCode);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not shut down the Avalonia lifetime");
            }
            finally
            {
                _shutdown.SetResult();
            }
        }
    }

    private void OnApplicationStopping()
    {
        // Ignored if the shutdown was requested here. Otherwise the host stopped by itself, e.g. because a
        // BackgroundService threw. That happens on a thread-pool thread, so move the shutdown to the UI thread.
        if (_uiContext is null)
        {
            _ = RequestShutdownAsync(ShutdownReason.Error);
        }
        else
        {
            _uiContext.Post(_ => _ = RequestShutdownAsync(ShutdownReason.Error), null);
        }
    }

    private void OnWatchdogElapsed(int exitCode)
    {
        _logger.LogError("Shutdown did not finish within {ExitTimeout}, ending the process", ExitTimeout);
        _exitProcess(exitCode);
    }
}
