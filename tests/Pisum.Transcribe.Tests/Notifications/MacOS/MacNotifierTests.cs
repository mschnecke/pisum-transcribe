using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Notifications;

namespace Pisum.Transcribe.Tests.Notifications;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class MacNotifierTests
{
    private readonly INotificationCenter _center = A.Fake<INotificationCenter>();
    private readonly CapturingLogger<MacNotifier> _logger = new();
    private readonly MacNotifier _sut;

    public MacNotifierTests()
    {
        _sut = new MacNotifier(_center, new InlineUiDispatcher(), _logger);
    }

    [Fact]
    public async Task StartAsync_InBundle_ReadsAuthorizationWithoutAsking()
    {
        // Arrange
        ReadAuthorizationReturns(NotificationAuthorization.NotDetermined);

        // Act
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        A.CallTo(() => _center.Start()).MustHaveHappenedOnceExactly();
        A.CallTo(() => _center.RequestAuthorization(A<Action<NotificationStatus>>._)).MustNotHaveHappened();
        _logger.Entries.ShouldContain(entry => entry.Message == "Notifications are not asked yet");
    }

    [Fact]
    public async Task Show_Allowed_AddsNotification()
    {
        // Arrange
        ReadAuthorizationReturns(NotificationAuthorization.Authorized);
        A.CallTo(() => _center.Notify(A<string>._, A<string>._)).Returns(NotificationStatus.Ok);
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        _sut.Show("Model ready", "Pisum Transcribe is ready.");

        // Assert
        A.CallTo(() => _center.Notify("Model ready", "Pisum Transcribe is ready.")).MustHaveHappenedOnceExactly();
        _logger.Entries.ShouldNotContain(entry => entry.Message.StartsWith("Notification:"));
    }

    [Fact]
    public async Task Show_NoBundle_WritesTitleAndMessageToLog()
    {
        // Arrange
        A.CallTo(() => _center.Start()).Returns(NotificationStatus.Unavailable);
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        _sut.Show("Model ready", "Pisum Transcribe is ready.");

        // Assert
        A.CallTo(() => _center.Notify(A<string>._, A<string>._)).MustNotHaveHappened();
        _logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Information && entry.Message == "Notification: Model ready: Pisum Transcribe is ready.");
    }

    [Fact]
    public async Task Show_Refused_LogsAtDebugAndDropsIt()
    {
        // Arrange
        ReadAuthorizationReturns(NotificationAuthorization.Denied);
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        _sut.Show("Model ready", "Pisum Transcribe is ready.");

        // Assert
        A.CallTo(() => _center.Notify(A<string>._, A<string>._)).MustNotHaveHappened();
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Debug && entry.Message.Contains("Model ready"));
    }

    private void ReadAuthorizationReturns(NotificationAuthorization authorization)
    {
        A.CallTo(() => _center.Start()).Returns(NotificationStatus.Ok);
        A.CallTo(() => _center.ReadAuthorization(A<Action<NotificationAuthorization>>._))
            .Invokes((Action<NotificationAuthorization> completed) => completed(authorization))
            .Returns(NotificationStatus.Ok);
    }
}
