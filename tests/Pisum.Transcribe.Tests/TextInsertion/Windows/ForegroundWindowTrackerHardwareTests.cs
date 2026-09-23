using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.TextInsertion;

/// <summary>
/// Reads the real foreground window. Needs an interactive desktop and a machine with UAC on.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Hardware)]
[Collection(DesktopCollection.Name)]
public sealed class ForegroundWindowTrackerHardwareTests
{
    private readonly ForegroundWindowTracker _sut = new(NullLogger<ForegroundWindowTracker>.Instance);

    [Fact(Explicit = true)]
    public void CaptureForeground_OwnWindow_ReturnsWindowAndProcess()
    {
        // Arrange
        using var window = TestWindow.Open();
        TextInsertionTestAssertions.ShouldBeForeground(window);

        // Act
        var target = _sut.CaptureForeground();

        // Assert
        target.WindowHandle.ShouldBe(window.Handle);
        target.ProcessId.ShouldBe(Environment.ProcessId);
        _sut.IsForeground(target).ShouldBeTrue();
    }

    [Fact(Explicit = true)]
    public void CaptureForeground_OwnWindowWithUacOn_ReportsNotElevated()
    {
        // Arrange
        Assert.SkipWhen(ProcessElevation.IsElevated((uint) Environment.ProcessId, out _) != false,
            "The test process runs elevated, for example because UAC is off, so every window reads as elevated.");
        using var window = TestWindow.Open();
        TextInsertionTestAssertions.ShouldBeForeground(window);

        // Act
        var target = _sut.CaptureForeground();

        // Assert
        target.ShouldBe(new InsertionTarget(window.Handle, Environment.ProcessId, false));
    }
}
