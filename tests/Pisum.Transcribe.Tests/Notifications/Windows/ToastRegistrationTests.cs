using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Notifications;
using Pisum.Transcribe.SettingsWindow;
using Pisum.Transcribe.Tests.SettingsWindow;

namespace Pisum.Transcribe.Tests.Notifications;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class ToastRegistrationTests
{
    private const string BaseDirectory = @"C:\Apps\Pisum Transcribe\";

    [Fact]
    public async Task Start_Always_WritesDisplayNameAndIconUri()
    {
        // Arrange
        var registry = new FakeUserRegistry();
        var sut = new ToastRegistration(registry, new CapturingLogger<ToastRegistration>(), BaseDirectory);

        // Act
        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        const string keyPath = @"Software\Classes\AppUserModelId\Pisum.Transcribe";
        registry.GetValue(keyPath, "DisplayName").ShouldBe("Pisum Transcribe");
        registry.GetValue(keyPath, "IconUri").ShouldBe(@"C:\Apps\Pisum Transcribe\TrayIcon.png");
    }

    [Fact]
    public async Task Start_RegistryThrows_LogsWarningAndContinues()
    {
        // Arrange
        var registry = A.Fake<IUserRegistry>();
        var exception = new UnauthorizedAccessException();
        A.CallTo(() => registry.SetValue(A<string>._, A<string>._, A<string>._)).Throws(exception);
        var logger = new CapturingLogger<ToastRegistration>();
        var sut = new ToastRegistration(registry, logger, BaseDirectory);

        // Act
        var start = sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        await start;
        start.IsCompletedSuccessfully.ShouldBeTrue();
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeSameAs(exception);
    }
}
