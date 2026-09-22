namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// Reads and writes values in the current user's registry hive (<c>HKEY_CURRENT_USER</c>).
/// </summary>
internal interface IUserRegistry
{
    /// <summary>
    /// Reads a value.
    /// </summary>
    /// <param name="keyPath">The key path below <c>HKEY_CURRENT_USER</c>.</param>
    /// <param name="name">The value name.</param>
    /// <returns>The value, or <see langword="null"/> if the key or the value does not exist.</returns>
    object? GetValue(string keyPath, string name);

    /// <summary>
    /// Writes a string value, creating the key if needed.
    /// </summary>
    /// <param name="keyPath">The key path below <c>HKEY_CURRENT_USER</c>.</param>
    /// <param name="name">The value name.</param>
    /// <param name="value">The value.</param>
    void SetValue(string keyPath, string name, string value);

    /// <summary>
    /// Deletes a value. A missing key or value is not an error.
    /// </summary>
    /// <param name="keyPath">The key path below <c>HKEY_CURRENT_USER</c>.</param>
    /// <param name="name">The value name.</param>
    void DeleteValue(string keyPath, string name);
}
