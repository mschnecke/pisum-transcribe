using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pisum.Transcribe.Recording;

/// <summary>
/// An AudioQueue input in 16 kHz mono float, pinned to one device (design D7 of add-macos-recording). AudioQueue
/// converts from the device's format. The callback runs on AudioQueue's own thread and reaches the queue through a
/// <see cref="GCHandle"/>, which is freed after the queue is disposed.
/// </summary>
internal sealed unsafe class AudioInputQueue : IAudioInputQueue
{
    private const int BufferCount = 3;
    private const int BufferMilliseconds = 50;
    private const int BytesPerSample = sizeof(float);

    // kAudioQueueProperty_CurrentDevice
    private static readonly uint CurrentDeviceProperty = CoreAudio.FourCc("aqcd");

    private readonly SamplesAvailableHandler _samplesAvailable;
    private readonly nint _queue;
    private GCHandle _handle;

    // Set before the queue is stopped, so the callback no longer enqueues buffers.
    private volatile bool _disposing;

    private AudioInputQueue(nint queue, GCHandle handle, SamplesAvailableHandler samplesAvailable)
    {
        _queue = queue;
        _handle = handle;
        _samplesAvailable = samplesAvailable;
    }

    /// <summary>
    /// Opens a queue on a device, with its buffers enqueued, not yet started.
    /// </summary>
    /// <param name="device">The device the queue is pinned to.</param>
    /// <param name="samplesAvailable">Receives the samples on AudioQueue's thread.</param>
    /// <returns>The queue.</returns>
    /// <exception cref="NoMicrophoneException">The device is gone.</exception>
    /// <exception cref="IOException">The queue could not be opened.</exception>
    public static AudioInputQueue Open(uint device, SamplesAvailableHandler samplesAvailable)
    {
        var format = new CoreAudio.AudioStreamBasicDescription
        {
            SampleRate = AudioClip.SampleRate,
            FormatId = CoreAudio.FourCc("lpcm"),
            // kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked
            FormatFlags = 0x1 | 0x8,
            BytesPerPacket = BytesPerSample,
            FramesPerPacket = 1,
            BytesPerFrame = BytesPerSample,
            ChannelsPerFrame = 1,
            BitsPerChannel = BytesPerSample * 8,
        };

        // The handle is set on the queue before the callback can run, because the queue isn't started yet.
        var handle = GCHandle.Alloc(null, GCHandleType.Normal);
        var callback = (nint) (delegate* unmanaged[Cdecl]<nint, nint, nint, nint, uint, nint, void>) &OnInput;
        var status = CoreAudio.AudioQueueNewInput(ref format, callback, GCHandle.ToIntPtr(handle), 0, 0, 0,
            out var queue);
        if (status != 0)
        {
            handle.Free();
            throw new IOException($"The microphone could not be opened (OSStatus {status}).");
        }

        var input = new AudioInputQueue(queue, handle, samplesAvailable);
        handle.Target = input;
        try
        {
            input.PinTo(device);
            input.EnqueueBuffers();
            return input;
        }
        catch
        {
            input.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Start()
    {
        var status = CoreAudio.AudioQueueStart(_queue, 0);
        if (status != 0)
        {
            throw new IOException($"The microphone could not be started (OSStatus {status}).");
        }
    }

    /// <summary>
    /// Stops the queue at once, disposes it and frees the handle, in that order. Calling it again has no effect.
    /// </summary>
    public void Dispose()
    {
        if (!_handle.IsAllocated)
        {
            return;
        }

        _disposing = true;
        CoreAudio.AudioQueueStop(_queue, true);
        CoreAudio.AudioQueueDispose(_queue, true);
        _handle.Free();
    }

    private void PinTo(uint device)
    {
        var uid = CoreAudio.CopyDeviceUid(device);
        if (uid == 0)
        {
            throw new NoMicrophoneException();
        }

        try
        {
            var status = CoreAudio.AudioQueueSetProperty(_queue, CurrentDeviceProperty, ref uid, (uint) nint.Size);
            if (status != 0)
            {
                throw new IOException(
                    $"The microphone could not be chosen for the recording (OSStatus {status}).");
            }
        }
        finally
        {
            CoreAudio.CFRelease(uid);
        }
    }

    private void EnqueueBuffers()
    {
        const uint byteSize = AudioClip.SampleRate * BufferMilliseconds / 1000 * BytesPerSample;
        for (var index = 0; index < BufferCount; index++)
        {
            var status = CoreAudio.AudioQueueAllocateBuffer(_queue, byteSize, out var buffer);
            if (status == 0)
            {
                status = CoreAudio.AudioQueueEnqueueBuffer(_queue, buffer, 0, 0);
            }

            if (status != 0)
            {
                throw new IOException(
                    $"The microphone's buffers could not be prepared (OSStatus {status}).");
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnInput(nint userData,
                                nint queue,
                                nint buffer,
                                nint startTime,
                                uint packetDescriptionCount,
                                nint packetDescriptions)
    {
        if (GCHandle.FromIntPtr(userData).Target is not AudioInputQueue input || input._disposing)
        {
            return;
        }

        try
        {
            input.Deliver(ref *(CoreAudio.AudioQueueBuffer*) buffer);
        }
        catch
        {
            // An exception must not unwind into AudioQueue. A handler that throws loses its packet.
        }

        if (!input._disposing)
        {
            CoreAudio.AudioQueueEnqueueBuffer(queue, buffer, 0, 0);
        }
    }

    private void Deliver(ref CoreAudio.AudioQueueBuffer buffer)
    {
        var count = (int) buffer.AudioDataByteSize / BytesPerSample;
        if (count > 0)
        {
            // CoreAudio marks no packet as silence, so silence is only what AudioRecorder finds all zero.
            _samplesAvailable(new ReadOnlySpan<float>((void*) buffer.AudioData, count), false);
        }
    }
}
