using Pisum.Transcribe.Hosting;
using Serilog;

namespace Pisum.Transcribe.Tests.Hosting;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class UnhandledExceptionHandlersTests
{
    [Fact]
    public void OnUnobservedTaskException_Exception_LogsErrorAndMarksObserved()
    {
        // Arrange
        var logger = A.Fake<ILogger>();
        var exception = new AggregateException(new InvalidOperationException("background failure"));
        var e = new UnobservedTaskExceptionEventArgs(exception);

        // Act
        UnhandledExceptionHandlers.OnUnobservedTaskException(logger, e);

        // Assert
        A.CallTo(() => logger.Error(exception, A<string>._)).MustHaveHappenedOnceExactly();
        e.Observed.ShouldBeTrue();
    }
}
