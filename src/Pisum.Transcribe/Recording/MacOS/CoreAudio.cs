using System.Runtime.InteropServices;

namespace Pisum.Transcribe.Recording;

/// <summary>
/// The C APIs of CoreAudio and of AudioToolbox's AudioQueue that the capture calls (design D7 of add-macos-recording).
/// </summary>
internal static class CoreAudio
{
    /// <summary>
    /// <c>kAudioObjectUnknown</c>, the device ID when there is no device.
    /// </summary>
    public const uint UnknownObject = 0;

    /// <summary>
    /// <c>kAudioHardwarePropertyDefaultInputDevice</c> of the system object, for a listener.
    /// </summary>
    public static readonly AudioObjectPropertyAddress DefaultInputDeviceAddress =
        new(FourCc("dIn "), FourCc("glob"), ElementMain);

    private const string CoreAudioPath = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    private const string AudioToolboxPath = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const string CoreFoundationPath = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // kAudioObjectSystemObject
    private const uint SystemObject = 1;

    // kAudioObjectPropertyElementMain
    private const uint ElementMain = 0;

    private static readonly AudioObjectPropertyAddress InputMuteAddress =
        new(FourCc("mute"), FourCc("inpt"), ElementMain);

    private static readonly AudioObjectPropertyAddress DeviceUidAddress =
        new(FourCc("uid "), FourCc("glob"), ElementMain);

    /// <summary>
    /// Reads the default input device.
    /// </summary>
    /// <returns>The device ID, or <see cref="UnknownObject"/> without an input device.</returns>
    public static uint GetDefaultInputDevice()
    {
        var address = DefaultInputDeviceAddress;
        var size = (uint) sizeof(uint);
        return AudioObjectGetPropertyData(SystemObject, ref address, 0, 0, ref size, out uint device) == 0
            ? device
            : UnknownObject;
    }

    /// <summary>
    /// Whether a device reports a mute state for its input.
    /// </summary>
    /// <param name="device">The device ID.</param>
    /// <returns><see langword="true"/> if the device has the mute property in the input scope.</returns>
    public static bool HasInputMute(uint device)
    {
        var address = InputMuteAddress;
        return AudioObjectHasProperty(device, ref address);
    }

    /// <summary>
    /// Whether a device's input is muted. A device without a mute state is never muted.
    /// </summary>
    /// <param name="device">The device ID.</param>
    /// <returns><see langword="true"/> if the mute property reads 1.</returns>
    public static bool IsInputMuted(uint device)
    {
        if (!HasInputMute(device))
        {
            return false;
        }

        var address = InputMuteAddress;
        var size = (uint) sizeof(uint);
        return AudioObjectGetPropertyData(device, ref address, 0, 0, ref size, out uint muted) == 0 && muted == 1;
    }

    /// <summary>
    /// Copies a device's UID, which pins an AudioQueue to the device. Release it with <see cref="CFRelease"/>.
    /// </summary>
    /// <param name="device">The device ID.</param>
    /// <returns>The UID as a <c>CFStringRef</c>, or 0 if it can't be read.</returns>
    public static nint CopyDeviceUid(uint device)
    {
        var address = DeviceUidAddress;
        var size = (uint) nint.Size;
        return AudioObjectGetPropertyData(device, ref address, 0, 0, ref size, out nint uid) == 0 ? uid : 0;
    }

    /// <summary>
    /// Adds a listener for a property of the system object.
    /// </summary>
    /// <param name="address">The property.</param>
    /// <param name="listener">An <c>AudioObjectPropertyListenerProc</c>.</param>
    /// <param name="clientData">The context passed to the listener.</param>
    /// <returns>The <c>OSStatus</c>, 0 on success.</returns>
    public static int AddSystemListener(AudioObjectPropertyAddress address, nint listener, nint clientData)
    {
        return AudioObjectAddPropertyListener(SystemObject, ref address, listener, clientData);
    }

    /// <summary>
    /// Removes a listener that <see cref="AddSystemListener"/> added. When it returns, the listener no longer runs.
    /// </summary>
    /// <param name="address">The property.</param>
    /// <param name="listener">The listener passed to <see cref="AddSystemListener"/>.</param>
    /// <param name="clientData">The context passed to <see cref="AddSystemListener"/>.</param>
    /// <returns>The <c>OSStatus</c>, 0 on success.</returns>
    public static int RemoveSystemListener(AudioObjectPropertyAddress address, nint listener, nint clientData)
    {
        return AudioObjectRemovePropertyListener(SystemObject, ref address, listener, clientData);
    }

    /// <summary>
    /// Turns a four-character code into its number, as the C headers write <c>'mute'</c>.
    /// </summary>
    /// <param name="code">Four ASCII characters.</param>
    /// <returns>The number.</returns>
    public static uint FourCc(string code)
    {
        return ((uint) code[0] << 24) | ((uint) code[1] << 16) | ((uint) code[2] << 8) | code[3];
    }

    [DllImport(CoreAudioPath)]
    private static extern int AudioObjectGetPropertyData(uint objectId,
                                                         ref AudioObjectPropertyAddress address,
                                                         uint qualifierDataSize,
                                                         nint qualifierData,
                                                         ref uint dataSize,
                                                         out uint data);

    [DllImport(CoreAudioPath)]
    private static extern int AudioObjectGetPropertyData(uint objectId,
                                                         ref AudioObjectPropertyAddress address,
                                                         uint qualifierDataSize,
                                                         nint qualifierData,
                                                         ref uint dataSize,
                                                         out nint data);

    [DllImport(CoreAudioPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool AudioObjectHasProperty(uint objectId, ref AudioObjectPropertyAddress address);

    [DllImport(CoreAudioPath)]
    private static extern int AudioObjectAddPropertyListener(uint objectId,
                                                             ref AudioObjectPropertyAddress address,
                                                             nint listener,
                                                             nint clientData);

    [DllImport(CoreAudioPath)]
    private static extern int AudioObjectRemovePropertyListener(uint objectId,
                                                                ref AudioObjectPropertyAddress address,
                                                                nint listener,
                                                                nint clientData);

    /// <summary>
    /// Creates an input queue. The callback runs on AudioQueue's own thread when <paramref name="runLoop"/> is 0.
    /// </summary>
    /// <param name="format">The format the queue delivers.</param>
    /// <param name="callback">An <c>AudioQueueInputCallback</c>.</param>
    /// <param name="userData">The context passed to the callback.</param>
    /// <param name="runLoop">The run loop of the callback, or 0.</param>
    /// <param name="runLoopMode">The run loop mode, or 0.</param>
    /// <param name="flags">Reserved, 0.</param>
    /// <param name="queue">The queue.</param>
    /// <returns>The <c>OSStatus</c>, 0 on success.</returns>
    [DllImport(AudioToolboxPath)]
    public static extern int AudioQueueNewInput(ref AudioStreamBasicDescription format,
                                                nint callback,
                                                nint userData,
                                                nint runLoop,
                                                nint runLoopMode,
                                                uint flags,
                                                out nint queue);

    /// <summary>
    /// Allocates a buffer of a queue.
    /// </summary>
    /// <param name="queue">The queue.</param>
    /// <param name="byteSize">The capacity in bytes.</param>
    /// <param name="buffer">The buffer, an <c>AudioQueueBufferRef</c>.</param>
    /// <returns>The <c>OSStatus</c>, 0 on success.</returns>
    [DllImport(AudioToolboxPath)]
    public static extern int AudioQueueAllocateBuffer(nint queue, uint byteSize, out nint buffer);

    /// <summary>
    /// Gives a buffer to an input queue to fill.
    /// </summary>
    /// <param name="queue">The queue.</param>
    /// <param name="buffer">The buffer.</param>
    /// <param name="packetDescriptionCount">0 for linear PCM.</param>
    /// <param name="packetDescriptions">0 for linear PCM.</param>
    /// <returns>The <c>OSStatus</c>, 0 on success.</returns>
    [DllImport(AudioToolboxPath)]
    public static extern int AudioQueueEnqueueBuffer(nint queue,
                                                     nint buffer,
                                                     uint packetDescriptionCount,
                                                     nint packetDescriptions);

    /// <summary>
    /// Starts a queue.
    /// </summary>
    /// <param name="queue">The queue.</param>
    /// <param name="startTime">0 to start at once.</param>
    /// <returns>The <c>OSStatus</c>, 0 on success.</returns>
    [DllImport(AudioToolboxPath)]
    public static extern int AudioQueueStart(nint queue, nint startTime);

    /// <summary>
    /// Stops a queue.
    /// </summary>
    /// <param name="queue">The queue.</param>
    /// <param name="immediate"><see langword="true"/> to stop at once, without the buffers already filled.</param>
    /// <returns>The <c>OSStatus</c>, 0 on success.</returns>
    [DllImport(AudioToolboxPath)]
    public static extern int AudioQueueStop(nint queue, [MarshalAs(UnmanagedType.U1)] bool immediate);

    /// <summary>
    /// Disposes a queue and its buffers. After it returns with <paramref name="immediate"/>, the callback no longer
    /// runs.
    /// </summary>
    /// <param name="queue">The queue.</param>
    /// <param name="immediate"><see langword="true"/> to dispose at once.</param>
    /// <returns>The <c>OSStatus</c>, 0 on success.</returns>
    [DllImport(AudioToolboxPath)]
    public static extern int AudioQueueDispose(nint queue, [MarshalAs(UnmanagedType.U1)] bool immediate);

    /// <summary>
    /// Sets a property of a queue that is a <c>CFStringRef</c>, such as <c>kAudioQueueProperty_CurrentDevice</c>.
    /// </summary>
    /// <param name="queue">The queue.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="value">The <c>CFStringRef</c>.</param>
    /// <param name="size">The size of a pointer.</param>
    /// <returns>The <c>OSStatus</c>, 0 on success.</returns>
    [DllImport(AudioToolboxPath)]
    public static extern int AudioQueueSetProperty(nint queue, uint propertyId, ref nint value, uint size);

    /// <summary>
    /// Releases a CoreFoundation object.
    /// </summary>
    /// <param name="reference">The object.</param>
    [DllImport(CoreFoundationPath)]
    public static extern void CFRelease(nint reference);

    /// <summary>
    /// <c>AudioObjectPropertyAddress</c>.
    /// </summary>
    /// <param name="Selector">The property.</param>
    /// <param name="Scope">The scope, such as input or global.</param>
    /// <param name="Element">The element, 0 for the main element.</param>
    [StructLayout(LayoutKind.Sequential)]
    public readonly record struct AudioObjectPropertyAddress(uint Selector, uint Scope, uint Element);

    /// <summary>
    /// <c>AudioStreamBasicDescription</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioStreamBasicDescription
    {
        /// <summary>Frames per second.</summary>
        public double SampleRate;

        /// <summary>The format, such as <c>kAudioFormatLinearPCM</c>.</summary>
        public uint FormatId;

        /// <summary>The format flags.</summary>
        public uint FormatFlags;

        /// <summary>Bytes per packet.</summary>
        public uint BytesPerPacket;

        /// <summary>Frames per packet.</summary>
        public uint FramesPerPacket;

        /// <summary>Bytes per frame.</summary>
        public uint BytesPerFrame;

        /// <summary>Channels per frame.</summary>
        public uint ChannelsPerFrame;

        /// <summary>Bits per channel.</summary>
        public uint BitsPerChannel;

        /// <summary>Reserved, 0.</summary>
        public uint Reserved;
    }

    /// <summary>
    /// The start of <c>AudioQueueBuffer</c>, as far as the capture reads it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioQueueBuffer
    {
        /// <summary>The capacity in bytes.</summary>
        public uint AudioDataBytesCapacity;

        /// <summary>The audio data.</summary>
        public nint AudioData;

        /// <summary>The bytes of audio data that the queue filled.</summary>
        public uint AudioDataByteSize;
    }
}
