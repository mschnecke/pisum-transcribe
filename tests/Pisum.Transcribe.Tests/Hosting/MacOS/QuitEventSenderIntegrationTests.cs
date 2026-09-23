using System.Diagnostics;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tests.Hosting;

/// <summary>
/// Calls the real Swift helper and libc.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class QuitEventSenderIntegrationTests
{
    [Fact]
    public void CurrentQuitSenderPid_OutsideQuitEvent_IsZero()
    {
        // Act
        var pid = PisumMac.CurrentQuitSenderPid();

        // Assert
        pid.ShouldBe(0);
    }

    [Fact]
    public void ProcessName_OwnPid_IsOwnProcessName()
    {
        // Arrange
        using var process = Process.GetCurrentProcess();

        // Act
        var name = PisumMac.ProcessName(Environment.ProcessId);

        // Assert
        name.ShouldBe(process.ProcessName);
    }
}
