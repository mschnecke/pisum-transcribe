using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pisum.Transcribe.VoiceActivity;

namespace Pisum.Transcribe.Tests.VoiceActivity;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class VoiceActivityServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddVoiceActivity_BuildProvider_ResolvesSileroDetectorAndWarmup()
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddVoiceActivity()
            .BuildServiceProvider(new ServiceProviderOptions {ValidateOnBuild = true, ValidateScopes = true});

        // Act
        var detector = provider.GetRequiredService<IVoiceActivityDetector>();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        // Assert
        detector.ShouldBeOfType<SileroVoiceActivityDetector>();
        hostedService.ShouldBeOfType<VoiceActivityWarmupService>();
    }
}
