using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Recording;

/// <summary>
/// Opens <see cref="AudioQueueCaptureSession"/>s on the macOS default input device (design D7 of add-macos-recording).
/// </summary>
/// <remarks>
/// macOS delivers digital zeros and no error for a denied microphone and for a muted device, so both are checked
/// before the microphone opens, in this order: the permission, the device, the mute state. A press of the hotkey never
/// shows macOS's microphone prompt, so a permission that wasn't asked yet counts as denied.
/// </remarks>
internal sealed class AudioQueueCaptureSessionFactory : ICaptureSessionFactory
{
    // The helper's pisum_microphone_status for an allowed microphone.
    private const int MicrophoneAllowed = 3;

    private readonly IAudioInput _input;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly Func<int> _readMicrophoneStatus;

    /// <summary>
    /// Initializes a new instance on CoreAudio and the Swift helper.
    /// </summary>
    /// <param name="library">The Swift helper, which reads the microphone permission.</param>
    /// <param name="uiDispatcher">Reaches the UI thread, where the helper is called.</param>
    public AudioQueueCaptureSessionFactory(MacNativeLibrary library, IUiDispatcher uiDispatcher)
        : this(new CoreAudioInput(), uiDispatcher, () => library.IsAvailable ? PisumMac.MicrophoneStatus() : 0)
    {
    }

    /// <summary>
    /// Initializes a new instance with other readers, for tests.
    /// </summary>
    /// <param name="input">The input devices and queues.</param>
    /// <param name="uiDispatcher">Reaches the UI thread, where the permission is read.</param>
    /// <param name="readMicrophoneStatus">
    /// Reads the microphone permission: 0 not asked yet, 1 restricted, 2 denied, 3 allowed.
    /// </param>
    internal AudioQueueCaptureSessionFactory(IAudioInput input, IUiDispatcher uiDispatcher, Func<int> readMicrophoneStatus)
    {
        _input = input;
        _uiDispatcher = uiDispatcher;
        _readMicrophoneStatus = readMicrophoneStatus;
    }

    /// <inheritdoc />
    public async Task<ICaptureSession> CreateAsync(CancellationToken cancellationToken)
    {
        var status = 0;
        await _uiDispatcher.InvokeAsync(() => status = _readMicrophoneStatus()).WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (status != MicrophoneAllowed)
        {
            throw new MicrophoneAccessDeniedException();
        }

        // Off the UI thread and the caller's thread, as opening an AudioQueue can take a moment.
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var device = _input.GetDefaultDevice();
            if (device == CoreAudio.UnknownObject)
            {
                throw new NoMicrophoneException();
            }

            // Checked only at start: opening a muted microphone would record silence and show it as in use.
            if (_input.IsMuted(device))
            {
                throw new MicrophoneMutedException();
            }

            return (ICaptureSession) new AudioQueueCaptureSession(_input, device);
        }, cancellationToken).ConfigureAwait(false);
    }
}
