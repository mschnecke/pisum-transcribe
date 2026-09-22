using Microsoft.Win32;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// The current user's registry hive.
/// </summary>
internal sealed class UserRegistry : IUserRegistry
{
    /// <inheritdoc />
    public object? GetValue(string keyPath, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(name);
    }

    /// <inheritdoc />
    public void SetValue(string keyPath, string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    /// <inheritdoc />
    public void DeleteValue(string keyPath, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, true);
        key?.DeleteValue(name, false);
    }
}
