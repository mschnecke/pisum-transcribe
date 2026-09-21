using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SettingsWindow;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Transcription;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.SettingsWindow;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class SettingsWindowServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddSettingsWindow_BuildProvider_RegistersStartupRegistrationApplierAndWindowAsHostedServices()
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(A.Fake<ITrayIconService>())
            .AddSingleton(A.Fake<IHostApplicationLifetime>())
            .AddSingleton(A.Fake<ISettingsStore>())
            .AddSingleton(A.Fake<IPushToTalkHotkey>())
            .AddSingleton(A.Fake<ITranscriber>())
            .AddSingleton(A.Fake<IModelStore>())
            .AddSettingsWindow()
            .BuildServiceProvider(new ServiceProviderOptions {ValidateOnBuild = true, ValidateScopes = true});

        // Act
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        // Assert
        hostedServices.Count.ShouldBe(3);
        hostedServices[0].ShouldBeSameAs(provider.GetRequiredService<IStartupRegistration>());
        hostedServices[1].ShouldBeOfType<SettingsApplier>();
        hostedServices[2].ShouldBeOfType<SettingsWindowService>();
    }
}
