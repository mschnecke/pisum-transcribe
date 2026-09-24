using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

/// <summary>
/// Calls the real Swift helper from the test host, which doesn't run as an app bundle.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class MacNativeLibraryIntegrationTests
{
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void AbiVersion_RealHelper_IsTheExpectedVersion()
    {
        // Act
        var version = PisumMac.AbiVersion();

        // Assert
        version.ShouldBe(2);
        version.ShouldBe(MacNativeLibrary.ExpectedAbiVersion);
    }

    [Fact]
    public void HasBundle_TestHost_Returns0()
    {
        // Act
        var hasBundle = PisumMac.HasBundle();

        // Assert
        hasBundle.ShouldBe(0);
    }

    [Fact]
    public void MicrophoneStatus_RealHelper_ReturnsAStatusFrom0To3()
    {
        // Act
        var status = PisumMac.MicrophoneStatus();

        // Assert
        status.ShouldBeInRange(0, 3);
    }

    [Fact]
    public async Task MicrophoneRequest_AlreadyAnswered_CallsBackWithTheStatus()
    {
        // Arrange: only when answered, so macOS shows no prompt. The test host's permission belongs to the terminal.
        var status = PisumMac.MicrophoneStatus();
        Assert.SkipWhen(status == 0, "The terminal wasn't asked for the microphone yet, so the request would prompt.");
        var completed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act
        PisumMac.MicrophoneRequest(PisumMac.Callback, PisumMac.CreateContext(completed.SetResult));

        // Assert
        (await completed.Task.WaitAsync(CallbackTimeout, TestContext.Current.CancellationToken)).ShouldBe(status);
    }

    [Fact]
    public void PasteboardAccessBehavior_RealHelper_ReturnsMinus1OrAValueFrom0To3()
    {
        // Act: the probe isn't called, because it may show an alert.
        var behavior = PisumMac.PasteboardAccessBehavior();

        // Assert
        behavior.ShouldBeInRange(-1, 3);
    }
}
