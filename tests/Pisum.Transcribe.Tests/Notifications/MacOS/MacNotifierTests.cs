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
    public async Task Show_Allowed_AddsNotification()
    {
        // Arrange
        A.CallTo(() => _center.Start(A<Action<NotificationStatus>>._))
            .Invokes((Action<NotificationStatus> completed) => completed(NotificationStatus.Ok))
            .Returns(NotificationStatus.Ok);
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
        A.CallTo(() => _center.Start(A<Action<NotificationStatus>>._)).Returns(NotificationStatus.Unavailable);
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
        A.CallTo(() => _center.Start(A<Action<NotificationStatus>>._))
            .Invokes((Action<NotificationStatus> completed) => completed(NotificationStatus.Denied))
            .Returns(NotificationStatus.Ok);
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Act
        _sut.Show("Model ready", "Pisum Transcribe is ready.");

        // Assert
        A.CallTo(() => _center.Notify(A<string>._, A<string>._)).MustNotHaveHappened();
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Debug && entry.Message.Contains("Model ready"));
    }
}
