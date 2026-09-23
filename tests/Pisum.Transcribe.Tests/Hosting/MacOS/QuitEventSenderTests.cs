using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class QuitEventSenderTests
{
    [Theory]
    [InlineData("loginwindow", nameof(ShutdownReason.SessionEnd))]
    [InlineData("osascript", nameof(ShutdownReason.UserExit))]
    [InlineData("Activity Monitor", nameof(ShutdownReason.UserExit))]
    public void ReadReason_Sender_IsSessionEndOnlyForLoginWindow(string sender, string expected)
    {
        // Arrange
        var sut = new QuitEventSender(() => 42, pid => pid == 42 ? sender : null, NullLogger<QuitEventSender>.Instance);

        // Act
        var reason = sut.ReadReason();

        // Assert
        reason.ToString().ShouldBe(expected);
    }

    [Fact]
    public void ReadReason_NoSender_IsUserExit()
    {
        // Arrange
        var sut = new QuitEventSender(() => 0, _ => "loginwindow", NullLogger<QuitEventSender>.Instance);

        // Act
        var reason = sut.ReadReason();

        // Assert
        reason.ShouldBe(ShutdownReason.UserExit);
    }
}
