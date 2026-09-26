using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Pisum.Transcribe.Notifications;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.Tests.SettingsWindow;
using Pisum.Transcribe.Tray;
using Pisum.Transcribe.Updates;

namespace Pisum.Transcribe.Tests.Updates;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class UpdateCheckServiceTests : IAsyncDisposable
{
    private const string RunningVersion = "1.1.1+b917e0ee";
#if WINDOWS
    private const string NoticeMessage = "Choose it in the tray menu to open the release page.";
#else
    private const string NoticeMessage = "Choose it in the menu bar to open the release page.";
#endif

    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(5);

    private readonly FakeHttpMessageHandler _handler = new() {Respond = _ => Release("v1.1.1")};
    private readonly ServiceProvider _services;
    private readonly CountingTimeProvider _time = new();
    private readonly ITrayIconService _trayIcon = A.Fake<ITrayIconService>();
    private readonly INotifier _notifier = A.Fake<INotifier>();
    private readonly CapturingLogger<UpdateCheckService> _logger = new();
    private readonly ConcurrentQueue<(string Title, string Message)> _notifications = new();
    private readonly List<string> _openedUrls = [];
    private FakeSettingsStore _settingsStore = new(new AppSettings());
    private UpdateCheckService? _sut;
    private Func<string>? _itemHeader;
    private Action? _itemClick;
    private Func<bool>? _itemIsVisible;

    public UpdateCheckServiceTests()
    {
        var services = new ServiceCollection();
        services.AddUpdates();

        // The client keeps the headers and timeout of AddUpdates and sends through the fake.
        services.AddHttpClient(UpdateCheckService.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _handler);
        _services = services.BuildServiceProvider();

        A.CallTo(() => _trayIcon.AddMenuItem(A<Func<string>>._, A<Action>._, A<Func<bool>?>._))
            .Invokes((Func<string> header, Action onClick, Func<bool>? isVisible) =>
            {
                _itemHeader = header;
                _itemClick = onClick;
                _itemIsVisible = isVisible;
            });
        A.CallTo(() => _notifier.Show(A<string>._, A<string>._))
            .Invokes((string title, string message) => _notifications.Enqueue((title, message)));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private UpdateCheckService Sut => _sut ??= new UpdateCheckService(
        _services.GetRequiredService<IHttpClientFactory>(), _settingsStore, _trayIcon, _notifier,
        new InlineUiDispatcher(), _time, _logger, FirstDelay, RunningVersion, _openedUrls.Add);

    private bool ItemShown => _itemIsVisible.ShouldNotBeNull()();

    private string ItemHeader => _itemHeader.ShouldNotBeNull()();

    private IEnumerable<LogEntry> Warnings => _logger.Entries.Where(entry => entry.Level == LogLevel.Warning);

    public async ValueTask DisposeAsync()
    {
        if (_sut is not null)
        {
            await _sut.StopAsync(CancellationToken.None).WaitAsync(SignalTimeout, CancellationToken.None);
            _sut.Dispose();
        }

        await _services.DisposeAsync();
    }

    [Fact]
    public async Task Start_BeforeFirstDelay_SendsNoRequest()
    {
        // Arrange
        await StartAsync();

        // Act
        _time.Advance(FirstDelay - TimeSpan.FromTicks(1));

        // Assert
        _handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Start_AfterFirstDelay_SendsOneRequest()
    {
        // Arrange
        await StartAsync();

        // Act
        _time.Advance(FirstDelay);
        await WaitForTimersAsync(2);

        // Assert
        _handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Check_After24Hours_SendsNextRequest()
    {
        // Arrange
        await StartAsync();
        _time.Advance(FirstDelay);
        await WaitForTimersAsync(2);
        _time.Advance(UpdateCheckService.CheckInterval - TimeSpan.FromTicks(1));
        var requestsBefore = _handler.Requests.Count;

        // Act
        _time.Advance(TimeSpan.FromTicks(1));
        await WaitForTimersAsync(3);

        // Assert
        requestsBefore.ShouldBe(1);
        _handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Check_Request_IsGetToLatestReleaseWithUserAgentWithoutVersion()
    {
        // Act
        await Sut.CheckOnceAsync(Ct);

        // Assert
        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.RequestUri.ShouldBe(new Uri("https://api.github.com/repos/mschnecke/pisum-transcribe/releases/latest"));
        request.Content.ShouldBeNull();
        request.Headers.Select(header => header.Key)
            .ShouldBe(["User-Agent", "Accept", "X-GitHub-Api-Version"], true);
        request.Headers.Contains("Cookie").ShouldBeFalse();
        request.Headers.Authorization.ShouldBeNull();
        request.Headers.UserAgent.ToString().ShouldBe("Pisum-Transcribe");
        request.Headers.UserAgent.ToString().ShouldNotContain("1.1");
        request.Headers.Accept.ToString().ShouldBe("application/vnd.github+json");
        request.Headers.GetValues("X-GitHub-Api-Version").ShouldBe(["2022-11-28"]);
    }

    [Fact]
    public async Task Check_OptionOff_SendsNoRequest()
    {
        // Arrange
        _settingsStore = new FakeSettingsStore(new AppSettings {Updates = new UpdateSettings(false)});
        await StartAsync();

        // Act
        _time.Advance(FirstDelay);
        await WaitForTimersAsync(2);
        _time.Advance(UpdateCheckService.CheckInterval);
        await WaitForTimersAsync(3);

        // Assert
        _handler.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    public async Task Check_HttpError_LogsWarningWithStatusCodeWithoutNotification(int statusCode)
    {
        // Arrange
        _handler.Respond = _ => new HttpResponseMessage((HttpStatusCode) statusCode)
        {
            Content = new StringContent("""{ "message": "API rate limit exceeded for 203.0.113.7." }"""),
        };
        await StartAsync();

        // Act
        await Sut.CheckOnceAsync(Ct);

        // Assert
        var warning = Warnings.ShouldHaveSingleItem();
        warning.Message.ShouldContain($"HTTP status {statusCode}");
        warning.Message.ShouldNotContain("203.0.113.7");
        _notifications.ShouldBeEmpty();
        ItemShown.ShouldBeFalse();
    }

    [Theory]
    [InlineData("network")]
    [InlineData("timeout")]
    public async Task Check_NetworkFailureOrTimeout_LogsWarningWithoutNotification(string failure)
    {
        // Arrange
        _handler.Respond = _ => failure == "network"
            ? throw new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known.")
            : throw new TaskCanceledException("The request was cancelled.", new TimeoutException());
        await StartAsync();

        // Act
        await Sut.CheckOnceAsync(Ct);

        // Assert
        Warnings.ShouldHaveSingleItem();
        _notifications.ShouldBeEmpty();
        ItemShown.ShouldBeFalse();
    }

    [Theory]
    [InlineData("""{ "tag_name": "v1.2.0-rc.1" }""")]
    [InlineData("""{ "tag_name": "release-7" }""")]
    [InlineData("""{ "tag_name": 12 }""")]
    [InlineData("""{ "name": "v1.2.0" }""")]
    [InlineData("""[ "v1.2.0" ]""")]
    [InlineData("null")]
    [InlineData("<html>v1.2.0</html>")]
    public async Task Check_TagUnreadable_LogsWarningWithoutNotification(string body)
    {
        // Arrange
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) {Content = new StringContent(body)};
        await StartAsync();

        // Act
        await Sut.CheckOnceAsync(Ct);

        // Assert
        var warning = Warnings.ShouldHaveSingleItem();
        warning.Message.ShouldNotContain("1.2.0");
        warning.Message.ShouldNotContain("release-7");
        _notifications.ShouldBeEmpty();
        ItemShown.ShouldBeFalse();
    }

    [Fact]
    public async Task Check_Failure_KeepsServiceRunningAndChecksNextDay()
    {
        // Arrange
        _handler.Respond = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "Unreachable.");
        await StartAsync();
        _time.Advance(FirstDelay);
        await WaitForTimersAsync(2);
        var runningAfterFailure = !Sut.ExecuteTask.ShouldNotBeNull().IsCompleted;
        _handler.Respond = _ => Release("v1.2.0");

        // Act
        _time.Advance(UpdateCheckService.CheckInterval);
        await WaitForTimersAsync(3);

        // Assert
        runningAfterFailure.ShouldBeTrue();
        Warnings.ShouldHaveSingleItem();
        _handler.Requests.Count.ShouldBe(2);
        ItemShown.ShouldBeTrue();
    }

    [Theory]
    [InlineData("delay")]
    [InlineData("request")]
    public async Task Stop_DuringDelayOrRequest_StopsAtOnce(string phase)
    {
        // Arrange
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ResponseBodyStream([], ResponseBodyStream.End.Stall)),
        };
        await StartAsync();
        if (phase == "request")
        {
            _time.Advance(FirstDelay);
            await WaitUntilAsync(() => _handler.Requests.Count == 1);
        }

        var stopwatch = Stopwatch.StartNew();

        // Act
        await Sut.StopAsync(CancellationToken.None).WaitAsync(SignalTimeout, Ct);

        // Assert
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        Sut.ExecuteTask.ShouldNotBeNull().IsCompleted.ShouldBeTrue();
        Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void AddUpdates_NamedClient_FollowsRedirects()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddUpdates();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
            .Get(UpdateCheckService.HttpClientName);
        var builder = provider.GetRequiredService<HttpMessageHandlerBuilder>();

        // Act
        foreach (var configure in options.HttpMessageHandlerBuilderActions)
        {
            configure(builder);
        }

        // Assert
        var handler = builder.PrimaryHandler.ShouldBeOfType<SocketsHttpHandler>();
        handler.AllowAutoRedirect.ShouldBeTrue();
        handler.UseCookies.ShouldBeFalse();
    }

    [Fact]
    public async Task Check_NewerRelease_ShowsItemWithVersionAndOneNotification()
    {
        // Arrange
        _handler.Respond = _ => Release("v1.2.0");
        await StartAsync();

        // Act
        await Sut.CheckOnceAsync(Ct);

        // Assert
        ItemShown.ShouldBeTrue();
        ItemHeader.ShouldBe("Pisum Transcribe 1.2.0 is available…");
        _notifications.ShouldBe([("Pisum Transcribe 1.2.0 is available", NoticeMessage)]);
        _logger.Entries.ShouldHaveSingleItem().Message
            .ShouldBe("The latest release is 1.2.0, and this is 1.1.1");
    }

    [Fact]
    public async Task Check_NoNewerReleaseAtStart_ShowsNothing()
    {
        // Arrange
        await StartAsync();

        // Act
        await Sut.CheckOnceAsync(Ct);

        // Assert
        ItemShown.ShouldBeFalse();
        _notifications.ShouldBeEmpty();
        Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Check_SameReleaseNextDay_NoSecondNotification()
    {
        // Arrange
        _handler.Respond = _ => Release("v1.2.0");
        await StartAsync();
        await Sut.CheckOnceAsync(Ct);

        // Act
        await Sut.CheckOnceAsync(Ct);

        // Assert
        ItemShown.ShouldBeTrue();
        ItemHeader.ShouldBe("Pisum Transcribe 1.2.0 is available…");
        _notifications.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Check_EvenNewerRelease_UpdatesItemAndNotifiesAgain()
    {
        // Arrange
        _handler.Respond = _ => Release("v1.2.0");
        await StartAsync();
        await Sut.CheckOnceAsync(Ct);
        _handler.Respond = _ => Release("v1.3.0");

        // Act
        await Sut.CheckOnceAsync(Ct);

        // Assert
        ItemHeader.ShouldBe("Pisum Transcribe 1.3.0 is available…");
        _notifications.Select(notification => notification.Title)
            .ShouldBe(["Pisum Transcribe 1.2.0 is available", "Pisum Transcribe 1.3.0 is available"]);
    }

    [Fact]
    public async Task Check_NoNewerRelease_HidesItem()
    {
        // Arrange
        _handler.Respond = _ => Release("v1.2.0");
        await StartAsync();
        await Sut.CheckOnceAsync(Ct);
        _handler.Respond = _ => Release("v1.1.1");

        // Act
        await Sut.CheckOnceAsync(Ct);

        // Assert
        ItemShown.ShouldBeFalse();
        _notifications.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Check_FailureAfterNotice_KeepsItem()
    {
        // Arrange
        _handler.Respond = _ => Release("v1.2.0");
        await StartAsync();
        await Sut.CheckOnceAsync(Ct);
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        // Act
        await Sut.CheckOnceAsync(Ct);

        // Assert
        Warnings.ShouldHaveSingleItem();
        ItemShown.ShouldBeTrue();
        ItemHeader.ShouldBe("Pisum Transcribe 1.2.0 is available…");
    }

    [Fact]
    public async Task MenuItem_Chosen_OpensReleasePageBuiltFromVersion()
    {
        // Arrange
        _handler.Respond = _ => Release("v1.2.0", "https://example.com/elsewhere");
        await StartAsync();
        await Sut.CheckOnceAsync(Ct);

        // Act
        _itemClick.ShouldNotBeNull()();

        // Assert
        _openedUrls.ShouldBe(["https://github.com/mschnecke/pisum-transcribe/releases/tag/v1.2.0"]);
    }

    [Fact]
    public async Task MenuItem_BrowserFails_LogsWarning()
    {
        // Arrange
        var sut = new UpdateCheckService(_services.GetRequiredService<IHttpClientFactory>(), _settingsStore, _trayIcon,
            _notifier, new InlineUiDispatcher(), _time, _logger, FirstDelay, RunningVersion,
            _ => throw new System.ComponentModel.Win32Exception(1155));
        _handler.Respond = _ => Release("v1.2.0");
        await sut.StartAsync(Ct);
        await sut.CheckOnceAsync(Ct);

        // Act
        var exception = Record.Exception(() => _itemClick.ShouldNotBeNull()());

        // Assert
        exception.ShouldBeNull();
        Warnings.ShouldHaveSingleItem();
        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SettingsChanged_TurnedOn_ChecksWithinOneMinute()
    {
        // Arrange
        _settingsStore = new FakeSettingsStore(new AppSettings {Updates = new UpdateSettings(false)});
        _handler.Respond = _ => Release("v1.2.0");
        await StartAsync();
        _time.Advance(FirstDelay);
        await WaitForTimersAsync(2);

        // Act
        await _settingsStore.SaveAsync(new AppSettings(), Ct);
        await WaitForTimersAsync(3);

        // Assert
        _handler.Requests.Count.ShouldBe(1);
        ItemShown.ShouldBeTrue();
        _notifications.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SettingsChanged_TurnedOff_HidesItemAndStopsChecks()
    {
        // Arrange
        _handler.Respond = _ => Release("v1.2.0");
        await StartAsync();
        _time.Advance(FirstDelay);
        await WaitForTimersAsync(2);
        var shownBefore = ItemShown;

        // Act
        await _settingsStore.SaveAsync(new AppSettings {Updates = new UpdateSettings(false)}, Ct);
        var shownAfter = ItemShown;
        _time.Advance(UpdateCheckService.CheckInterval);
        await WaitForTimersAsync(3);

        // Assert
        shownBefore.ShouldBeTrue();
        shownAfter.ShouldBeFalse();
        _handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SettingsChanged_OtherSetting_DoesNotCheck()
    {
        // Arrange
        await StartAsync();
        _time.Advance(FirstDelay);
        await WaitForTimersAsync(2);

        // Act
        await _settingsStore.SaveAsync(new AppSettings {VoiceActivity = new VoiceActivitySettings(false)}, Ct);

        // A wake-up would check at once, so a short wait is enough to see that none came.
        await Task.Delay(200, Ct);

        // Assert
        _handler.Requests.Count.ShouldBe(1);
        _time.TimerCount.ShouldBe(2);
    }

    private static HttpResponseMessage Release(string tag,
                                               string htmlUrl = "https://github.com/mschnecke/pisum-transcribe")
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{ "tag_name": "{{tag}}", "html_url": "{{htmlUrl}}", "prerelease": false, "body": "Notes" }""",
                Encoding.UTF8, "application/json"),
        };
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            stopwatch.Elapsed.ShouldBeLessThan(SignalTimeout);
            await Task.Delay(5, Ct);
        }
    }

    /// <summary>
    /// Starts the service and waits until its loop waits for the first check.
    /// </summary>
    private async Task StartAsync()
    {
        await Sut.StartAsync(Ct);
        await WaitForTimersAsync(1);
    }

    /// <summary>
    /// Waits until the loop has started <paramref name="count"/> waits in total, so an advance of the time reaches the
    /// latest one. Each wait after the first follows a check or a skipped check.
    /// </summary>
    private Task WaitForTimersAsync(int count)
    {
        return WaitUntilAsync(() => _time.TimerCount >= count);
    }

    /// <summary>
    /// Counts the timers, so a test knows when the loop waits.
    /// </summary>
    private sealed class CountingTimeProvider : FakeTimeProvider
    {
        private int _timerCount;

        public int TimerCount => Volatile.Read(ref _timerCount);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            Interlocked.Increment(ref _timerCount);
            return timer;
        }
    }
}
