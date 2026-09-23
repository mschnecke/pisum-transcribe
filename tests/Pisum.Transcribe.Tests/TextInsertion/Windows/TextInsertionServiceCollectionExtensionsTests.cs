using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.TextInsertion;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class TextInsertionServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddTextInsertion_BuildProvider_ResolvesTextInserterAndForegroundWindowTracker()
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddTextInsertion()
            .BuildServiceProvider(new ServiceProviderOptions {ValidateOnBuild = true, ValidateScopes = true});

        // Act
        var inserter = provider.GetRequiredService<ITextInserter>();
        var tracker = provider.GetRequiredService<IForegroundWindowTracker>();

        // Assert
        inserter.ShouldBeOfType<TextInserter>();
        tracker.ShouldBeOfType<ForegroundWindowTracker>();
    }

    [Fact]
    public async Task AddTextInsertion_BuildProvider_RegistersTextInserterAsHostedService()
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddTextInsertion()
            .BuildServiceProvider(new ServiceProviderOptions {ValidateOnBuild = true, ValidateScopes = true});

        // Act
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        // Assert
        hostedService.ShouldBeSameAs(provider.GetRequiredService<ITextInserter>());
    }
}
