using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.SettingsWindow;

namespace Pisum.Transcribe.Tests.SettingsWindow;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class StartupRegistrationTests
{
    private const string ProcessPath = @"C:\Apps\Pisum Transcribe\Pisum.Transcribe.exe";
    private const string Command = "\"" + ProcessPath + "\"";

    // Task Manager writes the first byte 0x03 to disable an entry and 0x02 to enable it again.
    private static readonly byte[] DisabledInTaskManager = [0x03, 0, 0, 0, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x01];
    private static readonly byte[] EnabledInTaskManager = [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    private readonly FakeUserRegistry _registry = new();
    private readonly StartupRegistration _sut;

    public StartupRegistrationTests()
    {
        _sut = new StartupRegistration(_registry, NullLogger<StartupRegistration>.Instance, ProcessPath);
    }

    [Fact]
    public void IsEnabled_NoStartupEntry_ReturnsFalse()
    {
        // Act
        var enabled = _sut.IsEnabled();

        // Assert
        enabled.ShouldBeFalse();
    }

    [Fact]
    public void SetEnabled_True_WritesQuotedProcessPath()
    {
        // Act
        _sut.SetEnabled(true);

        // Assert
        _registry.GetValue(StartupRegistration.RunKey, StartupRegistration.ValueName).ShouldBe(Command);
        _sut.IsEnabled().ShouldBeTrue();
    }

    [Fact]
    public void SetEnabled_False_DeletesStartupEntryAndTaskManagerValue()
    {
        // Arrange
        _sut.SetEnabled(true);
        _registry.Put(StartupRegistration.StartupApprovedKey, StartupRegistration.ValueName, EnabledInTaskManager);

        // Act
        _sut.SetEnabled(false);

        // Assert
        _registry.GetValue(StartupRegistration.RunKey, StartupRegistration.ValueName).ShouldBeNull();
        _registry.GetValue(StartupRegistration.StartupApprovedKey, StartupRegistration.ValueName).ShouldBeNull();
        _sut.IsEnabled().ShouldBeFalse();
    }

    [Fact]
    public void IsEnabled_DisabledInTaskManager_ReturnsFalse()
    {
        // Arrange
        _sut.SetEnabled(true);
        _registry.Put(StartupRegistration.StartupApprovedKey, StartupRegistration.ValueName, DisabledInTaskManager);

        // Act
        var enabled = _sut.IsEnabled();

        // Assert
        enabled.ShouldBeFalse();
    }

    [Fact]
    public void IsEnabled_EnabledAgainInTaskManager_ReturnsTrue()
    {
        // Arrange
        _sut.SetEnabled(true);
        _registry.Put(StartupRegistration.StartupApprovedKey, StartupRegistration.ValueName, EnabledInTaskManager);

        // Act
        var enabled = _sut.IsEnabled();

        // Assert
        enabled.ShouldBeTrue();
    }

    [Theory]
    [InlineData("not binary")]
    [InlineData(new byte[0])]
    public void IsEnabled_UnreadableTaskManagerValue_CountsAsEnabled(object approval)
    {
        // Arrange
        _sut.SetEnabled(true);
        _registry.Put(StartupRegistration.StartupApprovedKey, StartupRegistration.ValueName, approval);

        // Act
        var enabled = _sut.IsEnabled();

        // Assert
        enabled.ShouldBeTrue();
    }

    [Fact]
    public void SetEnabled_TrueAfterDisabledInTaskManager_RemovesOverride()
    {
        // Arrange
        _registry.Put(StartupRegistration.RunKey, StartupRegistration.ValueName, Command);
        _registry.Put(StartupRegistration.StartupApprovedKey, StartupRegistration.ValueName, DisabledInTaskManager);

        // Act
        _sut.SetEnabled(true);

        // Assert
        _registry.GetValue(StartupRegistration.StartupApprovedKey, StartupRegistration.ValueName).ShouldBeNull();
        _sut.IsEnabled().ShouldBeTrue();
    }

    [Fact]
    public async Task StartAsync_StartupEntryHasOtherPath_PointsItToCurrentExecutable()
    {
        // Arrange
        _registry.Put(StartupRegistration.RunKey, StartupRegistration.ValueName, "\"D:\\Old\\Pisum.Transcribe.exe\"");

        // Act
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        _registry.GetValue(StartupRegistration.RunKey, StartupRegistration.ValueName).ShouldBe(Command);
    }

    [Fact]
    public async Task StartAsync_NoStartupEntry_WritesNothing()
    {
        // Act
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        _registry.Writes.ShouldBeEmpty();
        _sut.IsEnabled().ShouldBeFalse();
    }

    [Fact]
    public async Task StartAsync_StartupEntryCurrent_WritesNothing()
    {
        // Arrange
        _registry.Put(StartupRegistration.RunKey, StartupRegistration.ValueName, Command);

        // Act
        await _sut.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        _registry.Writes.ShouldBeEmpty();
    }
}
