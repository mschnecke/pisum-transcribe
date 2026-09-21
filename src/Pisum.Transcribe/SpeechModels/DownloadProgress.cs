namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// The progress of a model download.
/// </summary>
/// <param name="BytesReceived">The bytes received so far.</param>
/// <param name="TotalBytes">The catalog size of the model.</param>
internal readonly record struct DownloadProgress(long BytesReceived, long TotalBytes);
