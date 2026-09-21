using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// One model download as a window shows it: progress, cancellation and the reason for a failure. Use it on the UI
/// thread.
/// </summary>
internal sealed partial class ModelDownloadViewModel : ObservableObject
{
    private readonly IModelStore _modelStore;
    private CancellationTokenSource? _cancellation;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="modelStore">The model store.</param>
    public ModelDownloadViewModel(IModelStore modelStore)
    {
        _modelStore = modelStore;
        ProgressText = string.Empty;
    }

    /// <summary>
    /// Whether a download is running.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDownloading { get; private set; }

    /// <summary>
    /// The download progress from 0 to 100.
    /// </summary>
    [ObservableProperty]
    public partial double ProgressPercent { get; private set; }

    /// <summary>
    /// The download progress as text, such as <c>0.50 of 1.07 GB</c>.
    /// </summary>
    [ObservableProperty]
    public partial string ProgressText { get; private set; }

    /// <summary>
    /// Why the last download failed, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>
    /// Whether the last download failed.
    /// </summary>
    public bool HasError => ErrorMessage is not null;

    /// <summary>
    /// Downloads and installs a model. A cancellation is not an error.
    /// </summary>
    /// <param name="model">The catalog model.</param>
    /// <param name="beforeDownload">
    /// Runs once the download shows as running and before any data is requested, such as saving the model as
    /// selected. A failure counts as a failed download.
    /// </param>
    /// <returns><see langword="true"/> if the model was installed.</returns>
    public async Task<bool> DownloadAsync(SpeechModel model, Func<Task>? beforeDownload = null)
    {
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        ErrorMessage = null;
        ProgressPercent = 0;
        ProgressText = ModelText.FormatProgress(0, model.SizeBytes);
        IsDownloading = true;

        try
        {
            if (beforeDownload is not null)
            {
                await beforeDownload();
            }

            await _modelStore.InstallAsync(model, new Progress<DownloadProgress>(OnProgress), cancellation.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the user, by closing the window or by the application stopping. Not an error.
            return false;
        }
        catch (Exception exception)
        {
            // The model store logs the details.
            ErrorMessage = DescribeError(model, exception);
            return false;
        }
        finally
        {
            _cancellation = null;
            IsDownloading = false;
        }
    }

    /// <summary>
    /// Cancels the running download. Has no effect while none runs.
    /// </summary>
    public void Cancel()
    {
        _cancellation?.Cancel();
    }

    private static string DescribeError(SpeechModel model, Exception exception)
    {
        var source = model.DownloadUrl.Host;
        return exception switch
        {
            ModelDownloadInProgressException => "This model is already downloading.",
            InsufficientDiskSpaceException diskSpace =>
                $"Not enough disk space. The download needs {ModelText.FormatSize(diskSpace.RequiredBytes)} of free " +
                $"space, but only {ModelText.FormatSize(diskSpace.AvailableBytes)} is free.",
            ModelIntegrityException =>
                $"The download from {source} failed because the file was corrupted. Try again.",
            HttpRequestException {StatusCode: { } statusCode} =>
                $"The download from {source} failed with HTTP status {(int) statusCode} ({statusCode}). Try again.",
            _ =>
                $"The download from {source} failed ({exception.GetType().Name}). Check the internet connection and try again.",
        };
    }

    private void OnProgress(DownloadProgress progress)
    {
        ProgressPercent = 100.0 * progress.BytesReceived / progress.TotalBytes;
        ProgressText = ModelText.FormatProgress(progress.BytesReceived, progress.TotalBytes);
    }
}
