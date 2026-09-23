using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pisum.Transcribe.Notifications;

namespace Pisum.Transcribe.Tests.Notifications;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class NotificationsServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddNotifications_BuildProvider_ResolvesToastNotifierAndRegistersToastRegistration()
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddNotifications()
            .BuildServiceProvider(new ServiceProviderOptions {ValidateOnBuild = true, ValidateScopes = true});

        // Act
        var notifier = provider.GetRequiredService<INotifier>();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        // Assert
        notifier.ShouldBeOfType<ToastNotifier>();
        hostedService.ShouldBeOfType<ToastRegistration>();
    }
}
