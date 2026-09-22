using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.Tray;

namespace Pisum.Transcribe.Updates;

/// <summary>
/// Asks GitHub for the latest stable release 1 to 10 minutes after the start and then every 24 hours, while "Check
/// for updates automatically" is on. When that release is newer than the running version, the tray menu shows
/// <b>Pisum Transcribe &lt;version&gt; is available…</b>, which opens the release page, and a notification announces
/// the version once per process.
/// </summary>
/// <remarks>
/// A failed check is logged as a warning and changes nothing; the next daily check tries again. The log names only
/// versions, HTTP status codes and durations, never the response body. Only the digits of a parsed tag reach the shell.
/// </remarks>
internal sealed class UpdateCheckService : BackgroundService
{
    /// <summary>
    /// The name of the <see cref="HttpClient"/> from <see cref="IHttpClientFactory"/>.
    /// <see cref="UpdatesServiceCollectionExtensions.AddUpdates"/> configures its headers, timeout and redirects.
    /// </summary>
    public const string HttpClientName = nameof(UpdateCheckService);

    /// <summary>
    /// The latest release of the repository in GitHub's API, which is never a draft or a pre-release.
    /// </summary>
    public static readonly Uri LatestReleaseUri =
        new("https://api.github.com/repos/mschnecke/pisum-transcript/releases/latest");

    /// <summary>
    /// The time between two checks.
    /// </summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// The time after which a request without an answer fails.
    /// </summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISettingsStore _settingsStore;
    private readonly ITrayIconService _trayIcon;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdateCheckService> _logger;
    private readonly TimeSpan _firstDelay;
    private readonly string? _runningVersionText;
    private readonly ReleaseVersion? _runningVersion;
    private readonly Action<string> _openUrl;
    private readonly Action<Action> _invokeOnUiThread;

    // Written when the option turns on, so the waiting loop checks at once. Holds at most one wake-up, and writing it
    // never blocks the thread that saved the settings.
    private readonly Channel<bool> _wakeUp =
        Channel.CreateBounded<bool>(new BoundedChannelOptions(1) {FullMode = BoundedChannelFullMode.DropWrite});

    // Keeps a check that ends while the option is turned off from showing the notice again.
    private readonly Lock _noticeLock = new();
    private readonly HashSet<ReleaseVersion> _announced = [];

    // The newer release that the last successful check found. Replaced as a whole, and read on the UI thread.
    private volatile ReleaseVersion? _available;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="httpClientFactory">Creates the <see cref="HttpClientName"/> client.</param>
    /// <param name="settingsStore">The settings store, read before each check.</param>
    /// <param name="trayIcon">The tray icon, which shows the notice.</param>
    /// <param name="timeProvider">The time provider for the delays between checks.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="firstDelay">
    /// The delay before the first check, for tests. <see langword="null"/> picks a random delay of 1 to 10 minutes.
    /// </param>
    /// <param name="runningVersion">
    /// The version of the running application, for tests. <see langword="null"/> uses the informational version of
    /// the application.
    /// </param>
    /// <param name="openUrl">
    /// Opens a web page, for tests. <see langword="null"/> opens it in the default browser.
    /// </param>
    /// <param name="invokeOnUiThread">
    /// Queues an action on the UI thread, for tests. <see langword="null"/> uses the WPF dispatcher.
    /// </param>
    public UpdateCheckService(IHttpClientFactory httpClientFactory,
                              ISettingsStore settingsStore,
                              ITrayIconService trayIcon,
                              TimeProvider timeProvider,
                              ILogger<UpdateCheckService> logger,
                              TimeSpan? firstDelay = null,
                              string? runningVersion = null,
                              Action<string>? openUrl = null,
                              Action<Action>? invokeOnUiThread = null)
    {
        _httpClientFactory = httpClientFactory;
        _settingsStore = settingsStore;
        _trayIcon = trayIcon;
        _timeProvider = timeProvider;
        _logger = logger;

        // Spread over 9 minutes, so the check doesn't compete with loading the model at sign-in, and many copies
        // behind one IP address don't all ask at the same moment.
        _firstDelay = firstDelay ?? TimeSpan.FromMinutes(1 + Random.Shared.NextDouble() * 9);

        // Without the build metadata, the commit after the +.
        _runningVersionText = (runningVersion ?? typeof(UpdateCheckService).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
            ?.Split('+', 2)[0];
        _runningVersion = ReleaseVersion.ParseOwn(_runningVersionText);
        _openUrl = openUrl ?? (url => Process.Start(new ProcessStartInfo(url) {UseShellExecute = true})?.Dispose());
        _invokeOnUiThread = invokeOnUiThread ?? (action => Application.Current.Dispatcher.InvokeAsync(action));
    }

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _settingsStore.Changed += OnSettingsChanged;
        _invokeOnUiThread(() => _trayIcon.AddMenuItem(() => $"Pisum Transcribe {_available} is available…",
            OpenReleasePage, () => _available is not null));
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _settingsStore.Changed -= OnSettingsChanged;
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks GitHub for the latest release, compares it with the running version and updates the notice. A failure is
    /// logged as a warning and leaves the notice as it is.
    /// </summary>
    /// <param name="stoppingToken">Cancelled when the application stops.</param>
    /// <returns>A task that completes when the check has succeeded or failed.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="stoppingToken"/> was cancelled.</exception>
    internal async Task CheckOnceAsync(CancellationToken stoppingToken)
    {
        if (_runningVersion is not { } running)
        {
            return;
        }

        var started = _timeProvider.GetTimestamp();
        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(LatestReleaseUri, stoppingToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // 404 means there is no stable release, 403 or 429 that the rate limit was reached.
                _logger.LogWarning("The update check failed with HTTP status {StatusCode} after {Duration}",
                    (int) response.StatusCode, _timeProvider.GetElapsedTime(started));
                return;
            }

            var latest = ReleaseVersion.ParseTag(await ReadTagAsync(response, stoppingToken).ConfigureAwait(false));
            if (latest is null)
            {
                _logger.LogWarning("The update check failed, because the tag of the latest release could not be read");
                return;
            }

            _logger.LogInformation("The latest release is {LatestVersion}, and this is {RunningVersion}", latest,
                _runningVersionText);
            UpdateNotice(latest, running);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // The client's timeout.
            _logger.LogWarning("The update check failed, because GitHub did not answer within {Duration}",
                _timeProvider.GetElapsedTime(started));
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning("The update check could not reach GitHub ({Error}) after {Duration}",
                exception.HttpRequestError, _timeProvider.GetElapsedTime(started));
        }
        catch (Exception exception)
        {
            // An exception that escapes would stop the host.
            _logger.LogWarning("The update check failed with {ExceptionType} after {Duration}",
                exception.GetType().Name, _timeProvider.GetElapsedTime(started));
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_runningVersion is null)
        {
            _logger.LogWarning("The update check is off, because the running version {RunningVersion} has no known form",
                _runningVersionText);
            return;
        }

        await WaitAsync(_firstDelay, stoppingToken).ConfigureAwait(false);
        while (true)
        {
            if (_settingsStore.Current.Updates.CheckAutomatically)
            {
                await CheckOnceAsync(stoppingToken).ConfigureAwait(false);
            }

            await WaitAsync(CheckInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads only <c>tag_name</c> from the release.
    /// </summary>
    /// <returns>The tag, or <see langword="null"/> if the response has none.</returns>
    private static async Task<string?> ReadTagAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var release = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return release.RootElement.ValueKind == JsonValueKind.Object
                   && release.RootElement.TryGetProperty("tag_name", out var tag)
                   && tag.ValueKind == JsonValueKind.String
                ? tag.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Waits for the delay, or until the option turns on.
    /// </summary>
    private async Task WaitAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var elapsed = Task.Delay(delay, _timeProvider, wait.Token);
        var wokenUp = _wakeUp.Reader.ReadAsync(wait.Token).AsTask();
        await Task.WhenAny(elapsed, wokenUp).ConfigureAwait(false);
        await wait.CancelAsync().ConfigureAwait(false);
        stoppingToken.ThrowIfCancellationRequested();
    }

    private void UpdateNotice(ReleaseVersion latest, ReleaseVersion running)
    {
        lock (_noticeLock)
        {
            if (!_settingsStore.Current.Updates.CheckAutomatically)
            {
                // Turned off during the check.
                return;
            }

            if (!latest.IsNewerThan(running))
            {
                _available = null;
                return;
            }

            _available = latest;
            if (!_announced.Add(latest))
            {
                return;
            }
        }

        _invokeOnUiThread(() => _trayIcon.ShowNotification($"Pisum Transcribe {latest} is available",
            "Choose it in the tray menu to open the release page."));
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        var wasOn = e.Previous.Updates.CheckAutomatically;
        var isOn = e.Current.Updates.CheckAutomatically;
        if (isOn && !wasOn)
        {
            _wakeUp.Writer.TryWrite(true);
        }
        else if (wasOn && !isOn)
        {
            lock (_noticeLock)
            {
                _available = null;
            }
        }
    }

    private void OpenReleasePage()
    {
        if (_available is not { } version)
        {
            return;
        }

        // Built from the parsed numbers, never from the response, so nothing else from GitHub reaches the shell.
        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://github.com/mschnecke/pisum-transcript/releases/tag/v{version.Major}.{version.Minor}.{version.Patch}");
        try
        {
            _openUrl(url);
        }
        catch (Exception exception)
        {
            // Such as when no browser is registered. Runs on the UI thread, where an exception would end the app.
            _logger.LogWarning(exception, "The release page of {Version} could not be opened", version);
        }
    }
}
