using Pisum.Transcribe.SettingsWindow;

namespace Pisum.Transcribe.Tests.SettingsWindow;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class MacLoginItemTests
{
    private readonly CapturingLogger<MacLoginItem> _logger = new();
    private int _status;
    private int _registerResult;
    private int _unregisterResult;
    private int _registerCalls;
    private int _unregisterCalls;

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, true, false)]
    [InlineData(2, false, true)]
    [InlineData(3, false, false)]
    public void IsEnabledAndRequiresApproval_Status_ReflectTheStatus(int status, bool enabled, bool requiresApproval)
    {
        // Arrange
        _status = status;
        var sut = CreateSut();

        // Act
        var isEnabled = sut.IsEnabled();
        var needsApproval = sut.RequiresApproval();

        // Assert
        isEnabled.ShouldBe(enabled);
        needsApproval.ShouldBe(requiresApproval);
    }

    [Fact]
    public void IsEnabledAndRequiresApproval_HelperUnavailable_ReturnFalseWithoutCallingIt()
    {
        // Arrange
        var sut = new MacLoginItem(false, () => throw new InvalidOperationException(), Register, Unregister, _logger);

        // Act
        var isEnabled = sut.IsEnabled();
        var needsApproval = sut.RequiresApproval();

        // Assert
        isEnabled.ShouldBeFalse();
        needsApproval.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void SetEnabled_TrueWhileNotEnabled_Registers(int status)
    {
        // Arrange
        _status = status;
        var sut = CreateSut();

        // Act
        sut.SetEnabled(true);

        // Assert
        _registerCalls.ShouldBe(1);
        _unregisterCalls.ShouldBe(0);
        _logger.Entries.ShouldHaveSingleItem().Message.ShouldBe("Open at login is now on");
    }

    [Fact]
    public void SetEnabled_TrueWhileEnabled_DoesNothing()
    {
        // Arrange
        _status = 1;
        var sut = CreateSut();

        // Act
        sut.SetEnabled(true);

        // Assert
        _registerCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SetEnabled_FalseWhileRegistered_Unregisters(int status)
    {
        // Arrange
        _status = status;
        var sut = CreateSut();

        // Act
        sut.SetEnabled(false);

        // Assert
        _unregisterCalls.ShouldBe(1);
        _registerCalls.ShouldBe(0);
        _logger.Entries.ShouldHaveSingleItem().Message.ShouldBe("Open at login is now off");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void SetEnabled_FalseWhileNotRegistered_DoesNothing(int status)
    {
        // Arrange
        _status = status;
        var sut = CreateSut();

        // Act
        sut.SetEnabled(false);

        // Assert
        _unregisterCalls.ShouldBe(0);
    }

    [Fact]
    public void SetEnabled_RegisterFails_ThrowsIOExceptionWithTheStatus()
    {
        // Arrange
        _registerResult = 6;
        var sut = CreateSut();

        // Act
        var exception = Should.Throw<IOException>(() => sut.SetEnabled(true));

        // Assert
        exception.Message.ShouldContain("error 6");
        _logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void SetEnabled_UnregisterFails_ThrowsIOExceptionWithTheStatus()
    {
        // Arrange
        _status = 1;
        _unregisterResult = 22;
        var sut = CreateSut();

        // Act
        var exception = Should.Throw<IOException>(() => sut.SetEnabled(false));

        // Assert
        exception.Message.ShouldContain("error 22");
    }

    [Fact]
    public void SetEnabled_HelperUnavailable_ThrowsIOException()
    {
        // Arrange
        var sut = new MacLoginItem(false, ReadStatus, Register, Unregister, _logger);

        // Act & Assert
        Should.Throw<IOException>(() => sut.SetEnabled(true));
        _registerCalls.ShouldBe(0);
    }

    private MacLoginItem CreateSut()
    {
        return new MacLoginItem(true, ReadStatus, Register, Unregister, _logger);
    }

    private int ReadStatus()
    {
        return _status;
    }

    private int Register()
    {
        _registerCalls++;
        return _registerResult;
    }

    private int Unregister()
    {
        _unregisterCalls++;
        return _unregisterResult;
    }
}
