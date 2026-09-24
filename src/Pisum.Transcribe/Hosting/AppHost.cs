using Microsoft.Extensions.DependencyInjection;
#if !WINDOWS
using Microsoft.Extensions.DependencyInjection.Extensions;
#endif
using Microsoft.Extensions.Hosting;
using Pisum.Transcribe.Dictation;
using Pisum.Transcribe.Notifications;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SettingsWindow;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.TextInsertion;
using Pisum.Transcribe.Transcription;
using Pisum.Transcribe.Tray;
using Pisum.Transcribe.Updates;
using Pisum.Transcribe.VoiceActivity;
#if !WINDOWS
using Pisum.Transcribe.Permissions;
#endif
using Serilog;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// Builds the Generic Host that holds the application's services.
/// </summary>
internal static class AppHost
{
    /// <summary>
    /// Creates the host. Each feature registers its services with one <c>services.Add&lt;Feature&gt;()</c> call here,
    /// on both platforms; only the permissions are macOS-only.
    /// Call it on the UI thread.
    /// </summary>
    /// <param name="paths">The application data folders.</param>
    /// <returns>The host, not yet started.</returns>
    public static IHost Create(AppPaths paths)
    {
        // The empty builder reads no appsettings.json or environment variables and adds no console or Event Log logging.
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        builder.Services.Configure<HostOptions>(options =>
        {
            // Leaves room inside the 5 s exit budget of ShutdownCoordinator.
            options.ShutdownTimeout = TimeSpan.FromSeconds(4);
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
        });
        builder.Services.AddSerilog((_, configuration) => configuration.WriteToAppLog(paths));
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton<IUiDispatcher>(new AvaloniaUiDispatcher());
#if WINDOWS
        builder.Services.AddSingleton<IProcessActivity, NoProcessActivity>();
#else
        builder.Services.AddSingleton<IHostLifetime, SignalFreeHostLifetime>();
        builder.Services.TryAddSingleton<MacNativeLibrary>();
        builder.Services.AddSingleton<QuitEventSender>();
        builder.Services.AddSingleton<IProcessActivity, MacProcessActivity>();
#endif

        builder.Services.AddTray();
        builder.Services.AddNotifications();
        builder.Services.AddSettings();
#if !WINDOWS
        builder.Services.AddPermissions();
#endif
        builder.Services.AddSpeechModels();
        builder.Services.AddTranscription();
        builder.Services.AddRecording();
        builder.Services.AddTextInsertion();
        builder.Services.AddVoiceActivity();
        builder.Services.AddDictation();
        builder.Services.AddSettingsWindow();
        builder.Services.AddUpdates();

        return builder.Build();
    }
}
