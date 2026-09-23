using Pisum.Transcribe.Notifications;
using Pisum.Transcribe.SettingsWindow;
using ToastNotificationManager = Windows.UI.Notifications.ToastNotificationManager;

namespace Pisum.Transcribe.Tests.Notifications;

/// <summary>
/// Shows a real toast, which needs a signed-in desktop session with notifications on for Pisum Transcribe. The toast has
/// no tag, which the history needs to remove one toast, so the test clears every Pisum Transcribe toast.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Hardware)]
public sealed class ToastNotifierHardwareTests
{
    [Fact(Explicit = true)]
    public async Task Show_RegisteredApplication_AddsToastToHistory()
    {
        // Arrange
        await new ToastRegistration(new UserRegistry(), new CapturingLogger<ToastRegistration>())
            .StartAsync(TestContext.Current.CancellationToken);
        var logger = new CapturingLogger<ToastNotifier>();
        var sut = new ToastNotifier(new WinRtToastSender(), logger);
        var title = $"Pisum Transcribe test {Guid.NewGuid():N}";

        // Act
        sut.Show(title, "A test notification. It removes itself.");

        // Assert
        logger.Entries.ShouldBeEmpty();
        var toast = ToastNotificationManager.History.GetHistory(ToastRegistration.AppUserModelId)
            .SingleOrDefault(toast => toast.Content.GetXml().Contains(title, StringComparison.Ordinal));
        toast.ShouldNotBeNull();
        ToastNotificationManager.History.Clear(ToastRegistration.AppUserModelId);
    }
}
