namespace Pisum.Transcribe.SpeechModels;

/// <summary>
/// The models folder's volume has not enough free space for a download.
/// </summary>
internal sealed class InsufficientDiskSpaceException : IOException
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="requiredBytes">The free space the download needs.</param>
    /// <param name="availableBytes">The free space on the volume.</param>
    public InsufficientDiskSpaceException(long requiredBytes, long availableBytes)
        : base($"The download needs {requiredBytes} bytes of free space, but {availableBytes} bytes are available.")
    {
        RequiredBytes = requiredBytes;
        AvailableBytes = availableBytes;
    }

    /// <summary>
    /// The free space the download needs.
    /// </summary>
    public long RequiredBytes { get; }

    /// <summary>
    /// The free space on the volume.
    /// </summary>
    public long AvailableBytes { get; }
}
