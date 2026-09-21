using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using SharpHook.Data;

namespace Pisum.Transcribe.Recording;

/// <summary>
/// Parses the push-to-talk hotkey setting into SharpHook key codes.
/// </summary>
internal static class HotkeyParser
{
    /// <summary>
    /// The key name of the default hotkey, the right Ctrl key held alone.
    /// </summary>
    public const string DefaultKeyName = nameof(KeyCode.VcRightControl);

    /// <summary>
    /// The default hotkey, the right Ctrl key held alone.
    /// </summary>
    public static readonly IReadOnlySet<KeyCode> DefaultHotkey = new[] {KeyCode.VcRightControl}.ToFrozenSet();

    // Exact names only: Enum.TryParse would also accept numbers and comma-separated lists.
    private static readonly FrozenDictionary<string, KeyCode> KeyCodesByName = Enum.GetNames<KeyCode>()
        .Where(name => name != nameof(KeyCode.VcUndefined))
        .ToFrozenDictionary(name => name, Enum.Parse<KeyCode>, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parses key names into a hotkey. Names are matched ignoring case, because the settings file writes camelCase.
    /// </summary>
    /// <param name="names">The key names from the settings.</param>
    /// <param name="logger">Receives a warning when the default is used instead of <paramref name="names"/>.</param>
    /// <returns>
    /// The keys, or <see cref="DefaultHotkey"/> if <paramref name="names"/> is <see langword="null"/>, empty or contains
    /// an unknown name.
    /// </returns>
    public static IReadOnlySet<KeyCode> Parse(IReadOnlyList<string>? names, ILogger logger)
    {
        if (names is {Count: > 0} && TryParse(names, out var hotkey))
        {
            return hotkey;
        }

        // Only the configured hotkey may appear in the log, never other keys.
        logger.LogWarning("The push-to-talk hotkey {Hotkey} is not valid, using the default {DefaultHotkey}",
            string.Join('+', names ?? []), DefaultKeyName);
        return DefaultHotkey;
    }

    private static bool TryParse(IReadOnlyList<string> names, out IReadOnlySet<KeyCode> hotkey)
    {
        var keys = new HashSet<KeyCode>();
        foreach (var name in names)
        {
            // A null element is possible when the file says so, despite the non-nullable type.
            if (name is null || !KeyCodesByName.TryGetValue(name, out var key))
            {
                hotkey = DefaultHotkey;
                return false;
            }

            keys.Add(key);
        }

        hotkey = keys.ToFrozenSet();
        return true;
    }
}
