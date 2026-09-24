using SharpHook.Data;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// Names hotkeys for display, such as <c>Left Ctrl+Left Win</c> on Windows or <c>Right Command</c> on macOS. The side of
/// a key that exists on both sides of the keyboard is always named, because the hotkey distinguishes the left and right
/// key.
/// </summary>
internal static class HotkeyText
{
    /// <summary>
    /// Formats a hotkey, modifiers first in the platform's order.
    /// </summary>
    /// <param name="keys">The keys of the hotkey.</param>
    /// <param name="names">The key names, <see langword="null"/> for <see cref="HotkeyKeyNames.Current"/>.</param>
    /// <returns>The hotkey, such as <c>Left Ctrl+Left Win</c> or <c>F13</c>.</returns>
    public static string Format(IEnumerable<KeyCode> keys, HotkeyKeyNames? names = null)
    {
        names ??= HotkeyKeyNames.Current;
        return string.Join('+', Order(keys, names).Select(names.Name));
    }

    /// <summary>
    /// Sorts keys as <see cref="Format"/> shows them, for a stable order in the settings file.
    /// </summary>
    /// <param name="keys">The keys.</param>
    /// <param name="names">The key names, <see langword="null"/> for <see cref="HotkeyKeyNames.Current"/>.</param>
    /// <returns>The keys, modifiers first.</returns>
    public static IEnumerable<KeyCode> Order(IEnumerable<KeyCode> keys, HotkeyKeyNames? names = null)
    {
        names ??= HotkeyKeyNames.Current;
        return keys.Distinct()
            .OrderBy(names.OrderOf)
            .ThenBy(key => key);
    }
}
