using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class SingleInstanceGuardTests
{
    private static readonly TimeSpan ShortWait = TimeSpan.FromMilliseconds(100);

    private readonly string _mutexName = $@"Local\Pisum.Transcribe.Tests.{Guid.NewGuid():N}";

    [Fact]
    public void TryAcquire_OwnedByOtherThread_TimesOut()
    {
        // Arrange
        var owner = new OwnerThread(_mutexName, true);
        using var sut = new SingleInstanceGuard(_mutexName);

        // Act
        var acquired = sut.TryAcquire(ShortWait);

        // Assert
        acquired.ShouldBeFalse();
        owner.End();
    }

    [Fact]
    public void TryAcquire_OtherThreadReleased_Succeeds()
    {
        // Arrange
        var owner = new OwnerThread(_mutexName, true);
        owner.End();
        using var sut = new SingleInstanceGuard(_mutexName);

        // Act
        var acquired = sut.TryAcquire(ShortWait);

        // Assert
        acquired.ShouldBeTrue();
    }

    [Fact]
    public void TryAcquire_OtherThreadEndedWithoutRelease_Succeeds()
    {
        // Arrange: open the mutex first, so it still exists when the owner abandons it.
        using var sut = new SingleInstanceGuard(_mutexName);
        var owner = new OwnerThread(_mutexName, false);
        owner.End();

        // Act
        var acquired = sut.TryAcquire(ShortWait);

        // Assert
        acquired.ShouldBeTrue();
    }

    /// <summary>
    /// Owns the mutex on a separate thread, because a mutex is re-entrant on the thread that owns it.
    /// </summary>
    private sealed class OwnerThread
    {
        private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);

        private readonly ManualResetEventSlim _acquired = new();
        private readonly ManualResetEventSlim _end = new();
        private readonly Thread _thread;

        public OwnerThread(string mutexName, bool releaseOnEnd)
        {
            _thread = new Thread(() =>
            {
                var guard = new SingleInstanceGuard(mutexName);
                if (guard.TryAcquire(TimeSpan.Zero))
                {
                    _acquired.Set();
                }

                _end.Wait(SignalTimeout);
                if (releaseOnEnd)
                {
                    guard.Dispose();
                }
            });
            _thread.Start();
            _acquired.Wait(SignalTimeout).ShouldBeTrue("the owner thread did not acquire the mutex");
        }

        public void End()
        {
            _end.Set();
            _thread.Join(SignalTimeout).ShouldBeTrue("the owner thread did not end");
        }
    }
}
