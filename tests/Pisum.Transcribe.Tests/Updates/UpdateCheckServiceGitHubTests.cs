using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.Tests.SettingsWindow;
using Pisum.Transcribe.Tray;
using Pisum.Transcribe.Updates;

namespace Pisum.Transcribe.Tests.Updates;

/// <summary>
/// Asks the real GitHub API for the latest release of the repository. Needs internet access.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Hardware)]
public sealed class UpdateCheckServiceGitHubTests
{
    [Fact(Explicit = true)]
    public async Task Check_RealGitHubApi_FindsNewerStableRelease()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddUpdates();
        await using var provider = services.BuildServiceProvider();
        var trayIcon = A.Fake<ITrayIconService>();
        Func<string>? header = null;
        A.CallTo(() => trayIcon.AddMenuItem(A<Func<string>>._, A<Action>._, A<Func<bool>?>._))
            .Invokes((Func<string> itemHeader, Action _, Func<bool>? _) => header = itemHeader);
        var logger = new CapturingLogger<UpdateCheckService>();
        using var sut = new UpdateCheckService(provider.GetRequiredService<IHttpClientFactory>(),
            new FakeSettingsStore(new AppSettings()), trayIcon, TimeProvider.System, logger,
            Timeout.InfiniteTimeSpan, "0.0.1", _ => { }, action => action());
        await sut.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            // Act
            await sut.CheckOnceAsync(TestContext.Current.CancellationToken);

            // Assert
            logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
            A.CallTo(() => trayIcon.ShowNotification(A<string>._, A<string>._)).MustHaveHappenedOnceExactly();
            header.ShouldNotBeNull()().ShouldMatch(@"^Pisum Transcribe \d+\.\d+\.\d+ is available…$");
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }
    }
}
