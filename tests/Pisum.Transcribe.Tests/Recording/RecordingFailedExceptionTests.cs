using Pisum.Transcribe.Recording;

namespace Pisum.Transcribe.Tests.Recording;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class RecordingFailedExceptionTests
{
#if WINDOWS
    [Fact]
    public void MicrophoneAccessDeniedException_Message_PointsToMicrophonePrivacySettingsAndDesktopApps()
    {
        // Act
        var message = new MicrophoneAccessDeniedException().Message;

        // Assert
        message.ShouldContain("Settings › Privacy & security › Microphone", Case.Sensitive);
        message.ShouldContain("desktop apps");
        message.ShouldContain("Pisum Transcribe");
    }
#else
    [Fact]
    public void MicrophoneAccessDeniedException_MessageOnMacOS_PointsToMicrophonePrivacySettings()
    {
        // Act
        var message = new MicrophoneAccessDeniedException().Message;

        // Assert
        message.ShouldContain("System Settings → Privacy & Security → Microphone", Case.Sensitive);
        message.ShouldContain("Pisum Transcribe");
    }
#endif

    [Fact]
    public void MicrophoneMutedException_Message_TellsUserToUnmute()
    {
        // Act
        var message = new MicrophoneMutedException().Message;

        // Assert
        message.ShouldContain("Unmute the microphone");
    }

    [Fact]
    public void AudioClip_Duration_IsSampleCountAtSixteenKilohertz()
    {
        // Act
        var clip = new AudioClip(new float[40_000]);

        // Assert
        clip.Duration.ShouldBe(TimeSpan.FromSeconds(2.5));
    }
}
