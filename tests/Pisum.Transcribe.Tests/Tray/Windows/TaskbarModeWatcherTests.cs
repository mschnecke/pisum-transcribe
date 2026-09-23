using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.Tray;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class TaskbarModeWatcherTests
{
    [Fact]
    public void TaskbarMode_ValueOne_IsLight()
    {
        // Arrange
        using var sut = CreateSut(new FakePersonalizeKey(1));

        // Act
        var mode = sut.Current;

        // Assert
        mode.ShouldBe(TaskbarMode.Light);
    }

    [Fact]
    public void TaskbarMode_ValueZero_IsDark()
    {
        // Arrange
        using var sut = CreateSut(new FakePersonalizeKey(0));

        // Act
        var mode = sut.Current;

        // Assert
        mode.ShouldBe(TaskbarMode.Dark);
    }

    [Fact]
    public void TaskbarMode_ValueMissing_IsDark()
    {
        // Arrange
        using var sut = CreateSut(new FakePersonalizeKey(null));

        // Act
        var mode = sut.Current;

        // Assert
        mode.ShouldBe(TaskbarMode.Dark);
    }

    [Fact]
    public void Changed_ModeFlips_IsRaisedWithNewMode()
    {
        // Arrange
        var key = new FakePersonalizeKey(1);
        using var sut = CreateSut(key);
        using var raised = new ManualResetEventSlim();
        TaskbarMode? modeWhenRaised = null;
        sut.Changed += (_, _) =>
        {
            modeWhenRaised = sut.Current;
            raised.Set();
        };
        key.WaitForArmCount(1);

        // Act
        key.Write(0);

        // Assert
        raised.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ShouldBeTrue();
        modeWhenRaised.ShouldBe(TaskbarMode.Dark);
    }

    [Fact]
    public void Changed_ValueRewrittenWithSameMode_IsNotRaised()
    {
        // Arrange
        var key = new FakePersonalizeKey(1);
        using var sut = CreateSut(key);
        using var raisedForDark = new ManualResetEventSlim();
        var raisedCount = 0;
        sut.Changed += (_, _) =>
        {
            Interlocked.Increment(ref raisedCount);
            if (sut.Current == TaskbarMode.Dark)
            {
                raisedForDark.Set();
            }
        };
        key.WaitForArmCount(1);

        // Act
        key.Write(1);
        key.WaitForArmCount(2);
        key.Write(0);

        // Assert: the watcher raises in order on one thread, so a change raised for the rewrite would come before the
        // one for the flip to dark.
        raisedForDark.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ShouldBeTrue();
        raisedCount.ShouldBe(1);
    }

    [Fact]
    public void Dispose_WhileWatching_StopsThread()
    {
        // Arrange
        var key = new FakePersonalizeKey(1);
        var sut = CreateSut(key);
        key.WaitForArmCount(1);

        // Act
        sut.Dispose();

        // Assert
        sut.IsWatching.ShouldBeFalse();
    }

    [Fact]
    public void Dispose_Twice_DoesNotThrow()
    {
        // Arrange: the container disposes the watcher once per registration, as TaskbarModeWatcher and as
        // ITaskbarModeWatcher.
        var key = new FakePersonalizeKey(1);
        var sut = CreateSut(key);
        key.WaitForArmCount(1);
        sut.Dispose();

        // Act
        var exception = Record.Exception(sut.Dispose);

        // Assert
        exception.ShouldBeNull();
    }

    private static TaskbarModeWatcher CreateSut(FakePersonalizeKey key)
    {
        return new TaskbarModeWatcher(key, NullLogger<TaskbarModeWatcher>.Instance);
    }
}
