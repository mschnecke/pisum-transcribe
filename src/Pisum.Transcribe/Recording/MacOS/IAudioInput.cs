namespace Pisum.Transcribe.Recording;

/// <summary>
/// The input devices and the queues of AudioQueue, the seam between the capture session and CoreAudio.
/// </summary>
internal interface IAudioInput
{
    /// <summary>
    /// Reads the default input device.
    /// </summary>
    /// <returns>The device ID, or <see cref="CoreAudio.UnknownObject"/> without an input device.</returns>
    uint GetDefaultDevice();

    /// <summary>
    /// Whether a device reports its input as muted. A device without a mute state is never muted.
    /// </summary>
    /// <param name="device">The device ID.</param>
    /// <returns><see langword="true"/> if muted.</returns>
    bool IsMuted(uint device);

    /// <summary>
    /// Calls an action on CoreAudio's notification thread whenever the default input device changes.
    /// </summary>
    /// <param name="changed">The action. It must return quickly.</param>
    /// <returns>Removes the listener when disposed. After that, the action no longer runs.</returns>
    IDisposable WatchDefaultDevice(Action changed);

    /// <summary>
    /// Opens an input queue on a device in 16 kHz mono float, not yet started.
    /// </summary>
    /// <param name="device">The device the queue is pinned to.</param>
    /// <param name="samplesAvailable">Receives the samples on AudioQueue's thread.</param>
    /// <returns>The queue. Disposing it stops it at once.</returns>
    /// <exception cref="NoMicrophoneException">The device is gone.</exception>
    /// <exception cref="IOException">The queue could not be opened.</exception>
    IAudioInputQueue OpenQueue(uint device, SamplesAvailableHandler samplesAvailable);
}

/// <summary>
/// One opened AudioQueue input. Dispose it to stop it and release the microphone.
/// </summary>
internal interface IAudioInputQueue : IDisposable
{
    /// <summary>
    /// Starts capturing.
    /// </summary>
    /// <exception cref="IOException">The queue could not be started.</exception>
    void Start();
}
