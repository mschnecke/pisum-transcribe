using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.Tray;
using Serilog;

namespace Pisum.Transcribe;

/// <summary>
/// The WPF application. It has no main window and lives in the system tray.
/// </summary>
internal sealed partial class App
{
    private readonly AppPaths _paths;

    // Referenced for the lifetime of the application, so its watchdog timer stays alive.
    private ShutdownCoordinator? _shutdownCoordinator;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="paths">The application data folders.</param>
    public App(AppPaths paths)
    {
        _paths = paths;
    }

    /// <inheritdoc />
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var host = AppHost.Create(_paths);
        var trayIcon = host.Services.GetRequiredService<ITrayIconService>();
        _shutdownCoordinator = new ShutdownCoordinator(
            host,
            host.Services.GetRequiredService<IHostApplicationLifetime>(),
            trayIcon,
            TimeProvider.System,
            Shutdown,
            ExitProcess,
            host.Services.GetRequiredService<ILogger<ShutdownCoordinator>>());
        Dispatcher.UnhandledException += _shutdownCoordinator.OnDispatcherUnhandledException;

        trayIcon.Show();
        host.Services.GetRequiredService<ISettingsStore>().Load();
        await host.StartAsync();
    }

    /// <inheritdoc />
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (_shutdownCoordinator is null)
        {
            return;
        }

        // Never cancelled, because that would veto the end of the session. Windows waits for this answer, so the
        // shutdown finishes before WPF answers, and WPF's own shutdown afterwards has no effect.
        DispatcherWait.Until(_shutdownCoordinator.RequestShutdownAsync(ShutdownReason.SessionEnd));
    }

    private static void ExitProcess(int exitCode)
    {
        Log.CloseAndFlush();

        // Not Environment.Exit: its process-exit handlers take about 330 ms, which would break the 5 s exit budget.
        using var process = Process.GetCurrentProcess();
        TerminateProcess(process.Handle, (uint) exitCode);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr processHandle, uint exitCode);
}
