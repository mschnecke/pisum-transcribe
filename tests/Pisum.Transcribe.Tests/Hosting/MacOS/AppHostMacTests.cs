using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pisum.Transcribe.Dictation;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Notifications;
using Pisum.Transcribe.Permissions;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.TextInsertion;
using Pisum.Transcribe.Tray;
using Pisum.Transcribe.VoiceActivity;

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
    public Task Create_MacOS_ResolvesHostedServicesTrayNotifierSettingsTextInsertionAndDictation()
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
            hostedServices.ShouldContain(service => service is SharpHookPushToTalkHotkey);
            host.Services.GetRequiredService<IPushToTalkHotkey>().ShouldBeOfType<SharpHookPushToTalkHotkey>();
            host.Services.GetRequiredService<IHotkeyKeyState>().ShouldBeOfType<MacHotkeyKeyState>();
            host.Services.GetRequiredService<IHookAccess>().ShouldBeOfType<MacHookAccess>();
            host.Services.GetRequiredService<ITextInserter>().ShouldBeOfType<TextInserter>();
            host.Services.GetRequiredService<IForegroundWindowTracker>().ShouldBeOfType<MacForegroundWindowTracker>();
            host.Services.GetRequiredService<IClipboardService>().ShouldBeOfType<MacClipboardService>();
            host.Services.GetRequiredService<IKeyboardInput>().ShouldBeOfType<MacKeyboardInput>();
            host.Services.GetRequiredService<ISecureInput>().ShouldBeOfType<MacSecureInput>();
            hostedServices.ShouldContain(service => service is TextInserter);
            hostedServices.ShouldContain(service => service is MacKeyboardInput);
            host.Services.GetRequiredService<IProcessActivity>().ShouldBeOfType<MacProcessActivity>();
            host.Services.GetRequiredService<IDictationState>().ShouldBeOfType<DictationState>();
            host.Services.GetRequiredService<IHotkeyAvailability>().ShouldBeOfType<MacHotkeyAvailability>();
            host.Services.GetRequiredService<IOverlayPlatform>().ShouldBeOfType<MacOverlayPlatform>();
            host.Services.GetRequiredService<IForegroundWindowTracker>()
                .ShouldBeSameAs(host.Services.GetRequiredService<MacForegroundWindowTracker>());
            hostedServices.ShouldContain(service => service is VoiceActivityWarmupService);
            hostedServices.ShouldContain(service => service is DictationController);

            // The availability starts before the feedback, which reads it at the tray's first render.
            hostedServices.FindIndex(service => service is MacHotkeyAvailability)
                .ShouldBeLessThan(hostedServices.FindIndex(service => service is DictationFeedback));
            notifier.ShouldBeOfType<MacNotifier>();
            settingsStore.ShouldNotBeNull();
            host.Services.GetRequiredService<QuitEventSender>().ShouldNotBeNull();
            host.Services.GetRequiredService<IHostLifetime>().ShouldBeOfType<SignalFreeHostLifetime>();

            // The test host doesn't run as an app bundle, so the permissions are skipped.
            host.Services.GetRequiredService<IPermissions>().ShouldBeOfType<MacPermissions>();
            host.Services.GetService<PermissionsViewModel>().ShouldBeNull();
            trayIcon.Remove();
        });
    }
}
