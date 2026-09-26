using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

/// <summary>
/// Begins activities through the real Swift helper from the test host.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class MacProcessActivityIntegrationTests
{
    [Fact]
    public void Begin_RealHelper_IsListedByPmsetUntilDisposedTwice()
    {
        // Arrange
        var reason = $"Pisum Transcribe test {Guid.NewGuid():N}";
        var sut = new MacProcessActivity(new MacNativeLibrary(new CapturingLogger<MacNativeLibrary>()),
            new CapturingLogger<MacProcessActivity>());

        // Act
        var activity = sut.Begin(reason);
        var whileRunning = Pmset.Assertions();
        activity.Dispose();
        activity.Dispose();
        var afterEnd = Pmset.Assertions();

        // Assert
        whileRunning.ShouldContain(reason);
        afterEnd.ShouldNotContain(reason);
    }
}
