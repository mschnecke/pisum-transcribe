using System.Runtime.InteropServices;
using Pisum.Transcribe.Recording;

namespace Pisum.Transcribe.Tests.Recording;

/// <summary>
/// Lists the input devices and changes the default input device and the mute state, for the hardware tests.
/// </summary>
internal static class CoreAudioTestDevices
{
    private const string CoreAudioPath = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";

    // kAudioObjectSystemObject
    private const uint SystemObject = 1;

    private static readonly CoreAudio.AudioObjectPropertyAddress DevicesAddress =
        new(CoreAudio.FourCc("dev#"), CoreAudio.FourCc("glob"), 0);

    private static readonly CoreAudio.AudioObjectPropertyAddress InputStreamsAddress =
        new(CoreAudio.FourCc("stm#"), CoreAudio.FourCc("inpt"), 0);

    private static readonly CoreAudio.AudioObjectPropertyAddress InputMuteAddress =
        new(CoreAudio.FourCc("mute"), CoreAudio.FourCc("inpt"), 0);

    /// <summary>
    /// The devices with at least one input stream.
    /// </summary>
    public static IReadOnlyList<uint> InputDevices()
    {
        var address = DevicesAddress;
        AudioObjectGetPropertyDataSize(SystemObject, ref address, 0, 0, out var size).ShouldBe(0);
        var devices = new uint[size / sizeof(uint)];
        AudioObjectGetPropertyData(SystemObject, ref address, 0, 0, ref size, devices).ShouldBe(0);
        return devices.Where(device =>
        {
            var streams = InputStreamsAddress;
            return AudioObjectGetPropertyDataSize(device, ref streams, 0, 0, out var streamsSize) == 0 &&
                   streamsSize > 0;
        }).ToList();
    }

    /// <summary>
    /// Makes a device the default input device, as a choice in System Settings → Sound does.
    /// </summary>
    public static void SetDefaultInputDevice(uint device)
    {
        var address = CoreAudio.DefaultInputDeviceAddress;
        AudioObjectSetPropertyData(SystemObject, ref address, 0, 0, sizeof(uint), ref device).ShouldBe(0);
    }

    /// <summary>
    /// Whether a device's input mute state can be set.
    /// </summary>
    public static bool CanSetInputMute(uint device)
    {
        var address = InputMuteAddress;
        return CoreAudio.HasInputMute(device) &&
               AudioObjectIsPropertySettable(device, ref address, out var settable) == 0 && settable;
    }

    /// <summary>
    /// Mutes or unmutes a device's input.
    /// </summary>
    public static void SetInputMute(uint device, bool muted)
    {
        var address = InputMuteAddress;
        var value = muted ? 1u : 0u;
        AudioObjectSetPropertyData(device, ref address, 0, 0, sizeof(uint), ref value).ShouldBe(0);
    }

    [DllImport(CoreAudioPath)]
    private static extern int AudioObjectGetPropertyDataSize(uint objectId,
                                                             ref CoreAudio.AudioObjectPropertyAddress address,
                                                             uint qualifierDataSize,
                                                             nint qualifierData,
                                                             out uint dataSize);

    [DllImport(CoreAudioPath)]
    private static extern int AudioObjectGetPropertyData(uint objectId,
                                                         ref CoreAudio.AudioObjectPropertyAddress address,
                                                         uint qualifierDataSize,
                                                         nint qualifierData,
                                                         ref uint dataSize,
                                                         [Out] uint[] data);

    [DllImport(CoreAudioPath)]
    private static extern int AudioObjectSetPropertyData(uint objectId,
                                                         ref CoreAudio.AudioObjectPropertyAddress address,
                                                         uint qualifierDataSize,
                                                         nint qualifierData,
                                                         uint dataSize,
                                                         ref uint data);

    [DllImport(CoreAudioPath)]
    private static extern int AudioObjectIsPropertySettable(uint objectId,
                                                            ref CoreAudio.AudioObjectPropertyAddress address,
                                                            [MarshalAs(UnmanagedType.U1)] out bool settable);
}
