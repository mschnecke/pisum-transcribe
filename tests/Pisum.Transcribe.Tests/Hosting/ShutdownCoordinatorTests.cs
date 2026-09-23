using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Notifications;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Tests.Hosting;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class ShutdownCoordinatorTests : IDisposable
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);

    private readonly IHost _host = A.Fake<IHost>();
    private readonly IHostApplicationLifetime _lifetime = A.Fake<IHostApplicationLifetime>();
    private readonly ITrayIconService _trayIcon = A.Fake<ITrayIconService>();
    private readonly INotifier _notifier = A.Fake<INotifier>();
    private readonly CancellationTokenSource _applicationStopping = new();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly List<int> _applicationShutdowns = [];

    private readonly TaskCompletionSource<int> _applicationShutdown =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly List<int> _processExits = [];
    private readonly CapturingLogger<ShutdownCoordinator> _logger = new();
    private readonly ShutdownCoordinator _sut;

    public ShutdownCoordinatorTests()
    {
        A.CallTo(() => _lifetime.ApplicationStopping).Returns(_applicationStopping.Token);

        _sut = new ShutdownCoordinator(
            _host,
            _lifetime,
            _trayIcon,
            _notifier,
            _timeProvider,
            exitCode =>
            {
                _applicationShutdowns.Add(exitCode);
                _applicationShutdown.TrySetResult(exitCode);
            },
            _processExits.Add,
            _logger);
    }

    public void Dispose()
    {
        _applicationStopping.Dispose();
    }

    [Fact]
    public async Task ExitRequested_Raised_RemovesIconBeforeStoppingHostAndExitsWithCode0()
    {
        // Act
        _trayIcon.ExitRequested += Raise.WithEmpty();

        // Assert
        (await _applicationShutdown.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken)).ShouldBe(0);
        A.CallTo(() => _trayIcon.Remove()).MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _host.StopAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly())
            .Then(A.CallTo(() => _host.Dispose()).MustHaveHappenedOnceExactly());
        A.CallTo(() => _notifier.Show(A<string>._, A<string>._)).MustNotHaveHappened();
    }

    [Theory]
    [InlineData(nameof(ShutdownReason.SessionEnd))]
    [InlineData(nameof(ShutdownReason.TerminationRequest))]
    public async Task RequestShutdownAsync_SessionEndOrTerminationRequest_RemovesIconBeforeStoppingHostAndExitsWithCode0(
        string reasonName)
    {
        // Arrange: by name, because the enum is internal.
        var reason = Enum.Parse<ShutdownReason>(reasonName);

        // Act
        await _sut.RequestShutdownAsync(reason)
            .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        _applicationShutdowns.ShouldBe([0]);
        A.CallTo(() => _trayIcon.Remove()).MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _host.StopAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly())
            .Then(A.CallTo(() => _host.Dispose()).MustHaveHappenedOnceExactly());
        A.CallTo(() => _notifier.Show(A<string>._, A<string>._)).MustNotHaveHappened();
        _logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Information
            && entry.Properties.Any(property => property.Key == "{OriginalFormat}"
                                                && Equals(property.Value, "Shutting down, reason {Reason}"))
            && entry.Properties.Any(property => property.Key == "Reason"
                                                && Equals(property.Value, reason)));
    }

    [Fact]
    public async Task RequestShutdownAsync_Error_NotifiesStopsHostThenRemovesIconWithoutWaiting()
    {
        // Act
        await _sut.RequestShutdownAsync(ShutdownReason.Error)
            .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        _applicationShutdowns.ShouldBe([1]);
        A.CallTo(() => _notifier.Show(A<string>._, A<string>._)).MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _host.StopAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly())
            .Then(A.CallTo(() => _trayIcon.Remove()).MustHaveHappenedOnceExactly())
            .Then(A.CallTo(() => _host.Dispose()).MustHaveHappenedOnceExactly());
    }

    [Fact]
    public async Task RequestShutdownAsync_CalledWhileRunning_ReturnsTheRunningShutdown()
    {
        // Arrange
        var hostStop = new TaskCompletionSource();
        A.CallTo(() => _host.StopAsync(A<CancellationToken>._)).Returns(hostStop.Task);

        // Act
        var first = _sut.RequestShutdownAsync(ShutdownReason.UserExit);
        var second = _sut.RequestShutdownAsync(ShutdownReason.SessionEnd);
        var secondCompletedEarly = second.IsCompleted;
        hostStop.SetResult();
        await Task.WhenAll(first, second).WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        secondCompletedEarly.ShouldBeFalse();
        _applicationShutdowns.ShouldBe([0]);
        A.CallTo(() => _host.StopAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ApplicationStopping_WithoutRequest_ExitsWithErrorCode1()
    {
        // Act
        await _applicationStopping.CancelAsync();

        // Assert
        (await AdvanceTimeUntilApplicationShutdownAsync()).ShouldBe(1);
        A.CallTo(() => _notifier.Show(A<string>._, A<string>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void RequestShutdownAsync_HostNeverStops_ExitsProcessAtExitTimeout()
    {
        // Arrange
        A.CallTo(() => _host.StopAsync(A<CancellationToken>._)).Returns(new TaskCompletionSource().Task);

        // Act
        var shutdown = _sut.RequestShutdownAsync(ShutdownReason.UserExit);
        _timeProvider.Advance(ShutdownCoordinator.ExitTimeout - TimeSpan.FromTicks(1));
        var exitsBeforeTimeout = _processExits.ToList();
        _timeProvider.Advance(TimeSpan.FromTicks(1));

        // Assert
        ShutdownCoordinator.ExitTimeout.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        exitsBeforeTimeout.ShouldBeEmpty();
        _processExits.ShouldBe([0]);
        shutdown.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task RequestShutdownAsync_WatchdogCannotStart_ShutsDownApplicationAndLogsError()
    {
        // Arrange
        var exception = new InvalidOperationException();
        var timeProvider = A.Fake<TimeProvider>();
        A.CallTo(() => timeProvider.CreateTimer(A<TimerCallback>._, A<object?>._, A<TimeSpan>._, A<TimeSpan>._))
            .Throws(exception);
        var sut = new ShutdownCoordinator(_host, _lifetime, _trayIcon, _notifier, timeProvider,
            _applicationShutdowns.Add, _processExits.Add, _logger);

        // Act
        await sut.RequestShutdownAsync(ShutdownReason.SessionEnd)
            .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        _applicationShutdowns.ShouldBe([0]);
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Error && entry.Exception == exception);
    }

    [Fact]
    public async Task RequestShutdownAsync_ApplicationShutdownThrows_CompletesAndLogsError()
    {
        // Arrange
        var exception = new InvalidOperationException();
        var sut = new ShutdownCoordinator(_host, _lifetime, _trayIcon, _notifier, _timeProvider,
            _ => throw exception, _processExits.Add, _logger);

        // Act
        await sut.RequestShutdownAsync(ShutdownReason.SessionEnd)
            .WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);

        // Assert
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Error && entry.Exception == exception);
    }

    /// <summary>
    /// Advances the fake time in small steps, because the shutdown may run on another thread and start waiting later.
    /// </summary>
    private async Task<int> AdvanceTimeUntilApplicationShutdownAsync()
    {
        for (var step = 0; step < 100 && !_applicationShutdown.Task.IsCompleted; step++)
        {
            _timeProvider.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        return await _applicationShutdown.Task.WaitAsync(SignalTimeout, TestContext.Current.CancellationToken);
    }
}
