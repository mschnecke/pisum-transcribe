#if !WINDOWS
using System.Runtime.InteropServices;
#endif
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Notifications;
#if !WINDOWS
using Pisum.Transcribe.Permissions;
#endif
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.Tray;
using Serilog;

namespace Pisum.Transcribe;

/// <summary>
/// The Avalonia application. It has no main window and lives in the system tray.
/// </summary>
internal sealed partial class App : Application
{
    private readonly AppPaths _paths;

    // Referenced for the lifetime of the application, so its watchdog timer stays alive.
    private ShutdownCoordinator? _shutdownCoordinator;

#if !WINDOWS
    private QuitEventSender? _quitEventSender;

    // Referenced for the lifetime of the application, so the handler stays registered.
    private PosixSignalRegistration? _terminationRequest;
#endif

    /// <summary>
    /// Initializes a new instance with the default data folders, for the XAML loader.
    /// </summary>
    public App()
        : this(new AppPaths())
    {
    }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="paths">The application data folders.</param>
    public App(AppPaths paths)
    {
        _paths = paths;
    }

    /// <inheritdoc />
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc />
    public override async void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();

        var lifetime = (IClassicDesktopStyleApplicationLifetime) ApplicationLifetime!;
        var host = AppHost.Create(_paths);
        var trayIcon = host.Services.GetRequiredService<ITrayIconService>();
#if WINDOWS
        Action? startNewInstance = null;
#else
        Action startNewInstance = () => AppBundle.StartNewInstance(AppBundle.FindPath(AppContext.BaseDirectory)
                                                                   ?? throw new InvalidOperationException(
                                                                       "No app bundle encloses the application."));
#endif
        _shutdownCoordinator = new ShutdownCoordinator(
            host,
            host.Services.GetRequiredService<IHostApplicationLifetime>(),
            trayIcon,
            host.Services.GetRequiredService<INotifier>(),
            TimeProvider.System,
            lifetime.Shutdown,
            ExitProcess,
            host.Services.GetRequiredService<ILogger<ShutdownCoordinator>>(),
            startNewInstance);
        Dispatcher.UIThread.UnhandledException += _shutdownCoordinator.OnDispatcherUnhandledException;
        lifetime.ShutdownRequested += OnShutdownRequested;
#if !WINDOWS
        _quitEventSender = host.Services.GetRequiredService<QuitEventSender>();
        host.Services.GetRequiredService<RelaunchService>().RelaunchRequested +=
            (_, _) => _ = _shutdownCoordinator.RequestShutdownAsync(ShutdownReason.Relaunch);

        // A termination request, for example from the installer, ends the application as Quit does, instead of the
        // default handling, which ends the process at once.
        _terminationRequest = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnTerminationRequested);
#endif

        trayIcon.Show();
        host.Services.GetRequiredService<ISettingsStore>().Load();
        await host.StartAsync();
    }

    private static void ExitProcess(int exitCode)
    {
        Log.CloseAndFlush();
        ProcessTermination.Exit(exitCode);
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_shutdownCoordinator is null)
        {
            return;
        }

#if WINDOWS
        // On Windows this is always the end of the session, including Restart Manager's ENDSESSION_CLOSEAPP: Exit calls
        // the coordinator directly, and there is no main window.
        var reason = ShutdownReason.SessionEnd;
#else
        // On macOS it's a quit event: from loginwindow when the session ends, or from another app such as Activity
        // Monitor. Quit Pisum Transcribe calls the coordinator directly.
        var reason = _quitEventSender!.ReadReason();
#endif

        // Never cancelled, because that would veto the end of the session. The system waits for this answer, so the
        // shutdown finishes before the handler returns.
        DispatcherWait.Until(_shutdownCoordinator.RequestShutdownAsync(reason));
    }
#if !WINDOWS

    private void OnTerminationRequested(PosixSignalContext context)
    {
        // On a thread-pool thread. At the end of the session macOS sends SIGTERM while the shutdown for the quit event
        // still runs; the coordinator then keeps that shutdown and its reason.
        context.Cancel = true;
        _ = Dispatcher.UIThread.InvokeAsync(
            () => _shutdownCoordinator!.RequestShutdownAsync(ShutdownReason.TerminationRequest));
    }
#endif
}
