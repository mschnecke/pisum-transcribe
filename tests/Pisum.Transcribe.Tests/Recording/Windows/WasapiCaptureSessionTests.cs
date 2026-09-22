using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using Pisum.Transcribe.Recording;

namespace Pisum.Transcribe.Tests.Recording;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class WasapiCaptureSessionTests
{
    private const int EAccessDenied = unchecked((int) 0x80070005);
    private const int ENotFound = unchecked((int) 0x80070490);

    [Fact]
    public void MapException_UnauthorizedAccess_ReturnsAccessDenied()
    {
        // Arrange
        var exception = new UnauthorizedAccessException();

        // Act
        var error = WasapiCaptureSession.MapException(exception);

        // Assert
        error.ShouldBeOfType<MicrophoneAccessDeniedException>().InnerException.ShouldBeSameAs(exception);
    }

    [Fact]
    public void MapException_AccessDeniedHResult_ReturnsAccessDenied()
    {
        // Act
        var error = WasapiCaptureSession.MapException(new COMException("Access denied.", EAccessDenied));

        // Assert
        error.ShouldBeOfType<MicrophoneAccessDeniedException>();
    }

    [Fact]
    public void MapException_NotFoundHResult_ReturnsNoMicrophone()
    {
        // Act
        var error = WasapiCaptureSession.MapException(new CoreAudioException(ENotFound));

        // Assert
        error.ShouldBeOfType<NoMicrophoneException>();
    }

    [Fact]
    public void MapException_OtherHResult_ReturnsNull()
    {
        // Act
        var error = WasapiCaptureSession.MapException(new COMException("Device invalidated.",
            unchecked((int) 0x88890004)));

        // Assert
        error.ShouldBeNull();
    }

    [Fact]
    public void MapException_RecordingFailedException_ReturnsNull()
    {
        // Act
        var error = WasapiCaptureSession.MapException(new MicrophoneMutedException());

        // Assert
        error.ShouldBeNull();
    }
}
