using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Notifications;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.Tray;
using Serilog;
using Windows.Win32;

namespace Pisum.Transcribe;

/// <summary>
/// The Avalonia application. It has no main window and lives in the system tray.
/// </summary>
internal sealed partial class App : Application
{
    private readonly AppPaths _paths;

    // Referenced for the lifetime of the application, so its watchdog timer stays alive.
    private ShutdownCoordinator? _shutdownCoordinator;

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
        _shutdownCoordinator = new ShutdownCoordinator(
            host,
            host.Services.GetRequiredService<IHostApplicationLifetime>(),
            trayIcon,
            host.Services.GetRequiredService<INotifier>(),
            TimeProvider.System,
            lifetime.Shutdown,
            ExitProcess,
            host.Services.GetRequiredService<ILogger<ShutdownCoordinator>>());
        Dispatcher.UIThread.UnhandledException += _shutdownCoordinator.OnDispatcherUnhandledException;
        lifetime.ShutdownRequested += OnShutdownRequested;

        trayIcon.Show();
        host.Services.GetRequiredService<ISettingsStore>().Load();
        await host.StartAsync();
    }

    private static void ExitProcess(int exitCode)
    {
        Log.CloseAndFlush();

        // Not Environment.Exit: its process-exit handlers take about 330 ms, which would break the 5 s exit budget.
        using var process = Process.GetCurrentProcess();
        PInvoke.TerminateProcess(process.SafeHandle, (uint) exitCode);
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_shutdownCoordinator is null)
        {
            return;
        }

        // On Windows this is always the end of the session, including Restart Manager's ENDSESSION_CLOSEAPP: Exit calls
        // the coordinator directly, and there is no main window. Never cancelled, because that would veto the end of the
        // session. Windows waits for this answer, so the shutdown finishes before the handler returns.
        DispatcherWait.Until(_shutdownCoordinator.RequestShutdownAsync(ShutdownReason.SessionEnd));
    }
}
