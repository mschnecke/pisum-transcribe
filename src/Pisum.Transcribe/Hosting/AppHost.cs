using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pisum.Transcribe.Dictation;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SettingsWindow;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.TextInsertion;
using Pisum.Transcribe.Transcription;
using Pisum.Transcribe.Tray;
using Pisum.Transcribe.Updates;
using Pisum.Transcribe.VoiceActivity;
using Serilog;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// Builds the Generic Host that holds the application's services.
/// </summary>
internal static class AppHost
{
    /// <summary>
    /// Creates the host. Each feature registers its services with one <c>services.Add&lt;Feature&gt;()</c> call here.
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

        builder.Services.AddTray();
        builder.Services.AddSettings();
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
