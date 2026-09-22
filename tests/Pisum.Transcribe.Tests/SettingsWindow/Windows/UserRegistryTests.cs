using Microsoft.Win32;
using Pisum.Transcribe.SettingsWindow;

namespace Pisum.Transcribe.Tests.SettingsWindow;

/// <summary>
/// Uses the real current-user registry, under a key of its own that is deleted afterwards.
/// </summary>
[Trait(Traits.Category, Traits.Categories.Integration)]
public sealed class UserRegistryTests : IDisposable
{
    private const string TestsKey = @"Software\Pisum Transcribe Tests";

    private readonly string _keyPath = $@"{TestsKey}\{Guid.NewGuid():N}";
    private readonly UserRegistry _sut = new();

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, false);
        using var testsKey = Registry.CurrentUser.OpenSubKey(TestsKey);
        if (testsKey is {SubKeyCount: 0, ValueCount: 0})
        {
            Registry.CurrentUser.DeleteSubKey(TestsKey, false);
        }
    }

    [Fact]
    public void GetValue_KeyMissing_ReturnsNull()
    {
        // Act
        var value = _sut.GetValue(_keyPath, "Missing");

        // Assert
        value.ShouldBeNull();
    }

    [Fact]
    public void SetValue_NewKey_CreatesKeyAndStoresString()
    {
        // Act
        _sut.SetValue(_keyPath, "Command", "\"C:\\App\\App.exe\"");

        // Assert
        _sut.GetValue(_keyPath, "Command").ShouldBe("\"C:\\App\\App.exe\"");
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
        key.ShouldNotBeNull().GetValueKind("Command").ShouldBe(RegistryValueKind.String);
    }

    [Fact]
    public void DeleteValue_ExistingValue_RemovesIt()
    {
        // Arrange
        _sut.SetValue(_keyPath, "Command", "value");

        // Act
        _sut.DeleteValue(_keyPath, "Command");

        // Assert
        _sut.GetValue(_keyPath, "Command").ShouldBeNull();
    }

    [Fact]
    public void DeleteValue_KeyOrValueMissing_DoesNotThrow()
    {
        // Arrange
        _sut.SetValue(_keyPath, "Other", "value");

        // Act
        _sut.DeleteValue(_keyPath, "Missing");
        _sut.DeleteValue(_keyPath + @"\Missing", "Missing");

        // Assert
        _sut.GetValue(_keyPath, "Other").ShouldBe("value");
    }
}
