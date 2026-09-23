using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Notifications;

/// <summary>
/// Calls the real Swift helper from the test host, which doesn't run as an app bundle.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class PisumNotificationCenterIntegrationTests
{
    [Fact]
    public void NotificationsStartAndNotify_NoBundle_ReturnNoBundleStatus()
    {
        // Arrange
        var called = false;

        // Act
        var start = PisumMac.NotificationsStart((_, _) => called = true, 0);
        var notify = PisumMac.Notify("Pisum Transcribe", "A test notification");

        // Assert
        start.ShouldBe(1);
        notify.ShouldBe(1);
        called.ShouldBeFalse();
    }
}
