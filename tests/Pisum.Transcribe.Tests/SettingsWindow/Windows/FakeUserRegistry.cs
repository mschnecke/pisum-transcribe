using Pisum.Transcribe.SettingsWindow;

namespace Pisum.Transcribe.Tests.SettingsWindow;

/// <summary>
/// An in-memory registry hive that records its writes.
/// </summary>
internal sealed class FakeUserRegistry : IUserRegistry
{
    private readonly Dictionary<(string KeyPath, string Name), object> _values = [];

    /// <summary>
    /// The writes and deletes in order, such as <c>Set Run</c> or <c>Delete StartupApproved</c>.
    /// </summary>
    public List<string> Writes { get; } = [];

    public object? GetValue(string keyPath, string name)
    {
        return _values.GetValueOrDefault((keyPath, name));
    }

    public void SetValue(string keyPath, string name, string value)
    {
        Writes.Add($"Set {Describe(keyPath)}");
        _values[(keyPath, name)] = value;
    }

    public void DeleteValue(string keyPath, string name)
    {
        Writes.Add($"Delete {Describe(keyPath)}");
        _values.Remove((keyPath, name));
    }

    /// <summary>
    /// Sets a value without recording a write, as another program would.
    /// </summary>
    public void Put(string keyPath, string name, object value)
    {
        _values[(keyPath, name)] = value;
    }

    private static string Describe(string keyPath)
    {
        return keyPath == StartupRegistration.RunKey ? "Run" : "StartupApproved";
    }
}
