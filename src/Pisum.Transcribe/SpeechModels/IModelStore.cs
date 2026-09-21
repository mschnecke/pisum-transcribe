using System.Net.Http;

namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// Finds, downloads and verifies catalog models in the models folder.
/// </summary>
internal interface IModelStore
{
    /// <summary>
    /// Raised after a model was downloaded, verified and installed. Raised on the thread that finished the download,
    /// so subscribers that touch WPF or the tray marshal to the dispatcher.
    /// </summary>
    event EventHandler<SpeechModel>? ModelInstalled;

    /// <summary>
    /// Gets the path of the installed model file, whether it exists or not.
    /// </summary>
    /// <param name="model">The catalog model.</param>
    /// <returns>The full path of the model file.</returns>
    string GetModelPath(SpeechModel model);

    /// <summary>
    /// Checks whether the model file exists with the catalog size. The hash is not recomputed, so the check is cheap
    /// enough for the UI thread.
    /// </summary>
    /// <param name="model">The catalog model.</param>
    /// <returns><see langword="true"/> if the model is installed.</returns>
    bool IsInstalled(SpeechModel model);

    /// <summary>
    /// Downloads the model, verifies its size and SHA-256 hash, and installs it. The model file appears only after the
    /// verification succeeded. The download is cancelled when the application stops. Only one download of a model runs
    /// at a time.
    /// </summary>
    /// <param name="model">The catalog model.</param>
    /// <param name="progress">Receives the bytes received, at most every 250 ms. The total is the catalog size.</param>
    /// <param name="cancellationToken">A token to cancel the download.</param>
    /// <returns>A task that completes when the model is installed.</returns>
    /// <exception cref="ModelDownloadInProgressException">
    /// Another download of the model is running. That download is not affected.
    /// </exception>
    /// <exception cref="InsufficientDiskSpaceException">The models folder's volume has not enough free space.</exception>
    /// <exception cref="ModelIntegrityException">The download has a different size or hash than the catalog.</exception>
    /// <exception cref="HttpRequestException">The request failed or returned an error status.</exception>
    /// <exception cref="TimeoutException">No data arrived for 60 seconds.</exception>
    /// <exception cref="OperationCanceledException">The download was cancelled.</exception>
    Task InstallAsync(SpeechModel model, IProgress<DownloadProgress> progress, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the model file, so the model is no longer installed. A missing file is not an error.
    /// </summary>
    /// <param name="model">The catalog model.</param>
    /// <exception cref="InvalidOperationException">The model is the selected model in the saved settings.</exception>
    /// <exception cref="IOException">The file could not be deleted, for example because it is in use.</exception>
    /// <exception cref="UnauthorizedAccessException">The file could not be deleted because access was denied.</exception>
    void Delete(SpeechModel model);
}
