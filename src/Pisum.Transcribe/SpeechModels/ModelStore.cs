using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Settings;

namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// Stores the catalog models in <see cref="AppPaths.ModelsDirectory"/>. At startup it deletes <c>*.partial</c> files
/// that a crash or the shutdown watchdog left behind. At startup and before each download it excludes the folder from
/// backups, where the platform has an exclusion.
/// </summary>
internal sealed class ModelStore : IModelStore, IHostedService
{
    /// <summary>
    /// The name of the <see cref="HttpClient"/> from <see cref="IHttpClientFactory"/>. Configure it without a timeout,
    /// because a download takes minutes; <see cref="InactivityTimeout"/> detects a stalled connection instead.
    /// </summary>
    public const string HttpClientName = nameof(ModelStore);

    /// <summary>
    /// The free space that must remain on the volume after a download.
    /// </summary>
    public const long FreeSpaceReserveBytes = 100 * 1024 * 1024;

    /// <summary>
    /// The time without received data after which a download fails.
    /// </summary>
    public static readonly TimeSpan InactivityTimeout = TimeSpan.FromSeconds(60);

    private const string PartialExtension = ".partial";
    private const int BufferSize = 81_920;

    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    private readonly string _modelsDirectory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISettingsStore _settingsStore;
    private readonly CancellationToken _applicationStopping;
    private readonly ILogger<ModelStore> _logger;
    private readonly Func<string, long> _getAvailableFreeSpace;
    private readonly Action<string>? _excludeFromBackup;

    // The identifiers of the models being downloaded.
    private readonly Lock _downloadsLock = new();
    private readonly HashSet<string> _downloads = [];

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="paths">The application data folders.</param>
    /// <param name="httpClientFactory">Creates the <see cref="HttpClientName"/> client.</param>
    /// <param name="lifetime">The application lifetime. Downloads are cancelled when the application stops.</param>
    /// <param name="settingsStore">The settings store, which names the selected model that cannot be deleted.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="getAvailableFreeSpace">
    /// Returns the free bytes on the volume of a folder. <see langword="null"/> uses <see cref="DriveInfo"/>.
    /// </param>
    /// <param name="excludeFromBackup">
    /// Excludes the existing models folder from backups, or <see langword="null"/> for none. A failure is logged.
    /// </param>
    public ModelStore(AppPaths paths,
                      IHttpClientFactory httpClientFactory,
                      IHostApplicationLifetime lifetime,
                      ISettingsStore settingsStore,
                      ILogger<ModelStore> logger,
                      Func<string, long>? getAvailableFreeSpace = null,
                      Action<string>? excludeFromBackup = null)
    {
        _modelsDirectory = paths.ModelsDirectory;
        _httpClientFactory = httpClientFactory;
        _settingsStore = settingsStore;
        _applicationStopping = lifetime.ApplicationStopping;
        _logger = logger;
        _getAvailableFreeSpace = getAvailableFreeSpace ?? (path => new DriveInfo(path).AvailableFreeSpace);
        _excludeFromBackup = excludeFromBackup;
    }

    /// <inheritdoc />
    public event EventHandler<SpeechModel>? ModelInstalled;

    /// <inheritdoc />
    public event EventHandler? DownloadStateChanged;

    /// <inheritdoc />
    public bool IsDownloading
    {
        get
        {
            lock (_downloadsLock)
            {
                return _downloads.Count > 0;
            }
        }
    }

    /// <inheritdoc />
    public string GetModelPath(SpeechModel model)
    {
        return Path.Combine(_modelsDirectory, model.FileName);
    }

    /// <inheritdoc />
    public bool IsInstalled(SpeechModel model)
    {
        var file = new FileInfo(GetModelPath(model));
        return file.Exists && file.Length == model.SizeBytes;
    }

    /// <inheritdoc />
    public async Task InstallAsync(SpeechModel model,
                                   IProgress<DownloadProgress> progress,
                                   CancellationToken cancellationToken)
    {
        bool isFirst;
        lock (_downloadsLock)
        {
            // Checked first, so the running download keeps its partial file.
            if (!_downloads.Add(model.Id))
            {
                _logger.LogInformation("Model {ModelId} is already downloading", model.Id);
                throw new ModelDownloadInProgressException(model.Id);
            }

            isFirst = _downloads.Count == 1;
        }

        try
        {
            if (isFirst)
            {
                DownloadStateChanged?.Invoke(this, EventArgs.Empty);
            }

            await DownloadAndInstallAsync(model, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            bool isLast;
            lock (_downloadsLock)
            {
                _downloads.Remove(model.Id);
                isLast = _downloads.Count == 0;
            }

            if (isLast)
            {
                DownloadStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <inheritdoc />
    public void Delete(SpeechModel model)
    {
        if (model.Id == ModelCatalog.Resolve(_settingsStore.Current.Model.SelectedModelId).Id)
        {
            throw new InvalidOperationException($"Model {model.Id} is the selected model and cannot be deleted.");
        }

        try
        {
            File.Delete(GetModelPath(model));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not delete model {ModelId}", model.Id);
            throw;
        }

        _logger.LogInformation("Deleted model {ModelId}", model.Id);
    }

    private async Task DownloadAndInstallAsync(SpeechModel model,
                                               IProgress<DownloadProgress> progress,
                                               CancellationToken cancellationToken)
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _applicationStopping);
        var token = cancellation.Token;

        Directory.CreateDirectory(_modelsDirectory);
        ExcludeFromBackup();
        var requiredBytes = model.SizeBytes + FreeSpaceReserveBytes;
        var availableBytes = _getAvailableFreeSpace(_modelsDirectory);
        if (availableBytes < requiredBytes)
        {
            _logger.LogWarning(
                "Not enough disk space to download model {ModelId}: {RequiredBytes} bytes needed, {AvailableBytes} bytes free",
                model.Id, requiredBytes, availableBytes);
            throw new InsufficientDiskSpaceException(requiredBytes, availableBytes);
        }

        var modelPath = GetModelPath(model);
        var partialPath = modelPath + PartialExtension;
        var started = Stopwatch.GetTimestamp();
        _logger.LogInformation("Downloading model {ModelId} from {Url}", model.Id, model.DownloadUrl);

        try
        {
            var hash = await DownloadAsync(model, partialPath, progress, token).ConfigureAwait(false);
            if (!string.Equals(hash, model.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new ModelIntegrityException(
                    $"The SHA-256 hash of the download is {hash}, but {model.Id} has {model.Sha256}.");
            }

            File.Move(partialPath, modelPath, true);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Download of model {ModelId} was cancelled", model.Id);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Download of model {ModelId} failed", model.Id);
            throw;
        }
        finally
        {
            TryDelete(partialPath);
        }

        _logger.LogInformation("Installed model {ModelId} in {Duration}", model.Id,
            Stopwatch.GetElapsedTime(started));
        ModelInstalled?.Invoke(this, model);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Created here, so the exclusion also covers a folder that an earlier version created without it.
        Directory.CreateDirectory(_modelsDirectory);
        ExcludeFromBackup();
        foreach (var partialFile in Directory.EnumerateFiles(_modelsDirectory, "*" + PartialExtension))
        {
            _logger.LogInformation("Deleting the unfinished download {PartialFile}", partialFile);
            TryDelete(partialFile);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Streams the response into <paramref name="partialPath"/> and hashes it while writing, so there is no second
    /// read pass over the file.
    /// </summary>
    /// <returns>The SHA-256 hash of the received data as lowercase hex.</returns>
    private async Task<string> DownloadAsync(SpeechModel model,
                                             string partialPath,
                                             IProgress<DownloadProgress> progress,
                                             CancellationToken token)
    {
        using var client = _httpClientFactory.CreateClient(HttpClientName);

        // Restarted before each wait, so a stalled connection fails while a slow one continues.
        using var inactivity = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            inactivity.CancelAfter(InactivityTimeout);
            using var response = await client
                .GetAsync(model.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, inactivity.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength is not null && contentLength != model.SizeBytes)
            {
                throw new ModelIntegrityException(
                    $"The source announced {contentLength} bytes, but {model.Id} has {model.SizeBytes} bytes.");
            }

            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using var file = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None,
                BufferSize, true);

            var buffer = new byte[BufferSize];
            var bytesReceived = 0L;
            progress.Report(new DownloadProgress(bytesReceived, model.SizeBytes));
            var lastReport = Stopwatch.GetTimestamp();

            while (true)
            {
                inactivity.CancelAfter(InactivityTimeout);
                var read = await body.ReadAsync(buffer, inactivity.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                bytesReceived += read;
                if (bytesReceived > model.SizeBytes)
                {
                    // Stops a wrong response, such as from a captive portal, before it fills the disk.
                    throw new ModelIntegrityException(
                        $"The source sent more than the {model.SizeBytes} bytes of {model.Id}.");
                }

                sha256.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);

                if (Stopwatch.GetElapsedTime(lastReport) >= ProgressInterval)
                {
                    progress.Report(new DownloadProgress(bytesReceived, model.SizeBytes));
                    lastReport = Stopwatch.GetTimestamp();
                }
            }

            // Written to disk before the move, so a crash cannot leave an installed file of the right size with lost data.
            file.Flush(true);
            return Convert.ToHexStringLower(sha256.GetHashAndReset());
        }
        catch (OperationCanceledException) when (inactivity.IsCancellationRequested && !token.IsCancellationRequested)
        {
            throw new TimeoutException($"No data was received from {model.DownloadUrl.Host} for {InactivityTimeout}.");
        }
    }

    private void ExcludeFromBackup()
    {
        if (_excludeFromBackup is null)
        {
            return;
        }

        try
        {
            _excludeFromBackup(_modelsDirectory);
        }
        catch (Exception exception)
        {
            // A model can always be downloaded again, so a backup of it only costs space.
            _logger.LogWarning(exception, "Could not exclude the models folder from backups");
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The next start deletes it.
            _logger.LogWarning(exception, "Could not delete {File}", path);
        }
    }
}
