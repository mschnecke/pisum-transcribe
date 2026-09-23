using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Notifications;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.Hosting;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class AppHostMacTests : IDisposable
{
    private readonly TempDirectory _root = new();

    public void Dispose()
    {
        _root.Dispose();
    }

    [Fact]
    public Task Create_MacOS_ResolvesHostedServicesTrayNotifierAndSettings()
    {
        return HeadlessUi.RunAsync(() =>
        {
            // Arrange
            using var host = AppHost.Create(new AppPaths(_root.Path));

            // Act
            var hostedServices = host.Services.GetServices<IHostedService>().ToList();
            var trayIcon = host.Services.GetRequiredService<ITrayIconService>();
            var notifier = host.Services.GetRequiredService<INotifier>();
            var settingsStore = host.Services.GetRequiredService<ISettingsStore>();

            // Assert
            hostedServices.ShouldNotBeEmpty();
            notifier.ShouldBeOfType<MacNotifier>();
            settingsStore.ShouldNotBeNull();
            host.Services.GetRequiredService<QuitEventSender>().ShouldNotBeNull();
            host.Services.GetRequiredService<IHostLifetime>().ShouldBeOfType<SignalFreeHostLifetime>();
            trayIcon.Remove();
        });
    }
}
