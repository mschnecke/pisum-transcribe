using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Notifications;

/// <summary>
/// Calls the real Swift helper from the test host, which doesn't run as an app bundle.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class PisumNotificationCenterIntegrationTests
{
    [Fact]
    public void NotificationFunctions_NoBundle_ReturnNoBundleStatus()
    {
        // Arrange
        var called = false;
        var requestContext = PisumMac.CreateContext(_ => called = true);
        var statusContext = PisumMac.CreateContext(_ => called = true);

        // Act
        var start = PisumMac.NotificationsStart();
        var request = PisumMac.NotificationsRequest(PisumMac.Callback, requestContext);
        var status = PisumMac.NotificationsStatus(PisumMac.Callback, statusContext);
        var notify = PisumMac.Notify("Pisum Transcribe", "A test notification");

        // Assert
        start.ShouldBe(1);
        request.ShouldBe(1);
        status.ShouldBe(1);
        notify.ShouldBe(1);
        called.ShouldBeFalse();
        PisumMac.FreeContext(requestContext);
        PisumMac.FreeContext(statusContext);
    }
}
