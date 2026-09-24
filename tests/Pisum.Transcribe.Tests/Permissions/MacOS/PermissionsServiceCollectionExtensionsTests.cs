using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Permissions;

namespace Pisum.Transcribe.Tests.Permissions;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class PermissionsServiceCollectionExtensionsTests
{
    private readonly CapturingLogger<PermissionsViewModel> _logger = new();
    private readonly ServiceProvider _provider;

    public PermissionsServiceCollectionExtensionsTests()
    {
        _provider = new ServiceCollection()
            .AddSingleton<ILogger<PermissionsViewModel>>(_logger)
            .AddSingleton(A.Fake<IPermissions>())
            .AddSingleton<IUiDispatcher>(new InlineUiDispatcher())
            .AddSingleton(TimeProvider.System)
            .BuildServiceProvider();
    }

    [Fact]
    public void CreateViewModel_NoBundle_ReturnsNullAndLogs()
    {
        // Act
        var viewModel = PermissionsServiceCollectionExtensions.CreateViewModel(_provider, () => 0);

        // Assert
        viewModel.ShouldBeNull();
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Information
                                              && entry.Message.Contains("Permissions are skipped"));
    }

    [Fact]
    public void CreateViewModel_Bundle_ReturnsViewModelWithoutLogging()
    {
        // Act
        var viewModel = PermissionsServiceCollectionExtensions.CreateViewModel(_provider, () => 1);

        // Assert
        viewModel.ShouldNotBeNull();
        _logger.Entries.ShouldBeEmpty();
    }
}
