using Pisum.Transcribe.Recording;

namespace Pisum.Transcribe.Tests.Recording;

/// <summary>
/// Reads the real input devices, which a machine without a microphone may not have.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class CoreAudioIntegrationTests
{
    [Fact]
    public void GetDefaultInputDevice_RealSystem_ReadsTheDeviceAndItsMuteState()
    {
        // Act
        var device = CoreAudio.GetDefaultInputDevice();
        var hasMute = CoreAudio.HasInputMute(device);
        var isMuted = CoreAudio.IsInputMuted(device);

        // Assert
        TestContext.Current.TestOutputHelper?.WriteLine($"Device {device}, has mute {hasMute}, muted {isMuted}");
        if (device == CoreAudio.UnknownObject)
        {
            hasMute.ShouldBeFalse();
            isMuted.ShouldBeFalse();
        }
        else
        {
            var uid = CoreAudio.CopyDeviceUid(device);
            uid.ShouldNotBe(0);
            CoreAudio.CFRelease(uid);
        }
    }

    [Fact]
    public void FourCc_Mute_IsTheHeaderValue()
    {
        // Act
        var code = CoreAudio.FourCc("mute");

        // Assert
        code.ShouldBe(0x6D757465u);
    }
}
