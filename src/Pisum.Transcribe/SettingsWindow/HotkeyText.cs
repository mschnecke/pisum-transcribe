using SharpHook.Data;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// Names hotkeys for display, such as <c>Left Ctrl+Left Win</c>. The side of a key that exists on both sides of the
/// keyboard is always named, because the hotkey distinguishes the left and right key.
/// </summary>
internal static class HotkeyText
{
    private static readonly KeyCode[] ModifierOrder =
    [
        KeyCode.VcLeftControl, KeyCode.VcRightControl, KeyCode.VcLeftAlt, KeyCode.VcRightAlt,
        KeyCode.VcLeftShift, KeyCode.VcRightShift, KeyCode.VcLeftMeta, KeyCode.VcRightMeta,
    ];

    private static readonly Dictionary<KeyCode, string> ModifierNames = new()
    {
        [KeyCode.VcLeftControl] = "Left Ctrl",
        [KeyCode.VcRightControl] = "Right Ctrl",
        [KeyCode.VcLeftAlt] = "Left Alt",
        [KeyCode.VcRightAlt] = "Right Alt",
        [KeyCode.VcLeftShift] = "Left Shift",
        [KeyCode.VcRightShift] = "Right Shift",
        [KeyCode.VcLeftMeta] = "Left Win",
        [KeyCode.VcRightMeta] = "Right Win",
    };

    /// <summary>
    /// Formats a hotkey, modifiers first in the order Ctrl, Alt, Shift, Win.
    /// </summary>
    /// <param name="keys">The keys of the hotkey.</param>
    /// <returns>The hotkey, such as <c>Left Ctrl+Left Win</c> or <c>F13</c>.</returns>
    public static string Format(IEnumerable<KeyCode> keys)
    {
        return string.Join('+', Order(keys).Select(Name));
    }

    /// <summary>
    /// Sorts keys as <see cref="Format"/> shows them, for a stable order in the settings file.
    /// </summary>
    /// <param name="keys">The keys.</param>
    /// <returns>The keys, modifiers first.</returns>
    public static IEnumerable<KeyCode> Order(IEnumerable<KeyCode> keys)
    {
        return keys.Distinct()
            .OrderBy(key => Array.IndexOf(ModifierOrder, key) is var index and >= 0 ? index : ModifierOrder.Length)
            .ThenBy(key => key);
    }

    private static string Name(KeyCode key)
    {
        if (ModifierNames.TryGetValue(key, out var name))
        {
            return name;
        }

        var keyName = key.ToString();
        return keyName.StartsWith("Vc", StringComparison.Ordinal) ? keyName[2..] : keyName;
    }
}
