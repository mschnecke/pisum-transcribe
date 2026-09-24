using System.Collections.Frozen;
using SharpHook.Data;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// The hotkey editor's key names and valid-hotkey rule of one platform. Plain data, so both platforms' tables can be
/// tested on either host.
/// </summary>
internal sealed class HotkeyKeyNames
{
    private readonly KeyCode[] _modifierOrder;
    private readonly FrozenDictionary<KeyCode, string> _modifierNames;

    private HotkeyKeyNames((KeyCode Key, string Name)[] modifiers, string rejectedMessage, bool showsFnHint)
    {
        _modifierOrder = modifiers.Select(modifier => modifier.Key).ToArray();
        _modifierNames = modifiers.ToFrozenDictionary(modifier => modifier.Key, modifier => modifier.Name);

        // By name, because the key codes of F1-F24 are not contiguous.
        RequiredKeys =
        [
            .._modifierOrder,
            ..Enumerable.Range(1, 24).Select(number => Enum.Parse<KeyCode>($"VcF{number}")),
        ];
        RejectedMessage = rejectedMessage;
        ShowsFnHint = showsFnHint;
    }

    /// <summary>
    /// The Windows names: modifiers in the order Ctrl, Alt, Shift, Win.
    /// </summary>
    public static HotkeyKeyNames Windows { get; } = new(
        [
            (KeyCode.VcLeftControl, "Left Ctrl"), (KeyCode.VcRightControl, "Right Ctrl"),
            (KeyCode.VcLeftAlt, "Left Alt"), (KeyCode.VcRightAlt, "Right Alt"),
            (KeyCode.VcLeftShift, "Left Shift"), (KeyCode.VcRightShift, "Right Shift"),
            (KeyCode.VcLeftMeta, "Left Win"), (KeyCode.VcRightMeta, "Right Win"),
        ],
        "The hotkey must include Ctrl, Alt, Shift, the Windows key or a function key F1–F24.",
        showsFnHint: false);

    /// <summary>
    /// The macOS names: modifiers in Apple's order fn, Control, Option, Shift, Command, spelled out in words.
    /// </summary>
    public static HotkeyKeyNames MacOS { get; } = new(
        [
            (KeyCode.VcFunction, "fn"),
            (KeyCode.VcLeftControl, "Left Control"), (KeyCode.VcRightControl, "Right Control"),
            (KeyCode.VcLeftAlt, "Left Option"), (KeyCode.VcRightAlt, "Right Option"),
            (KeyCode.VcLeftShift, "Left Shift"), (KeyCode.VcRightShift, "Right Shift"),
            (KeyCode.VcLeftMeta, "Left Command"), (KeyCode.VcRightMeta, "Right Command"),
        ],
        "The hotkey must include Control, Option, Shift, Command, fn or a function key F1–F24.",
        showsFnHint: true);

    /// <summary>
    /// The names of the platform the app runs on.
    /// </summary>
#if WINDOWS
    public static HotkeyKeyNames Current => Windows;
#else
    public static HotkeyKeyNames Current => MacOS;
#endif

    /// <summary>
    /// The keys of which a hotkey must include at least one: the modifiers and F1–F24.
    /// </summary>
    public FrozenSet<KeyCode> RequiredKeys { get; }

    /// <summary>
    /// The validation message for a hotkey without any of <see cref="RequiredKeys"/>.
    /// </summary>
    public string RejectedMessage { get; }

    /// <summary>
    /// Whether a hotkey with fn shows the hint about macOS's "Press 🌐 key to" setting.
    /// </summary>
    public bool ShowsFnHint { get; }

    /// <summary>
    /// Names a key, with its side for a modifier that exists on both sides of the keyboard.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The name, such as <c>Right Command</c> or <c>F13</c>.</returns>
    public string Name(KeyCode key)
    {
        if (_modifierNames.TryGetValue(key, out var name))
        {
            return name;
        }

        var keyName = key.ToString();
        return keyName.StartsWith("Vc", StringComparison.Ordinal) ? keyName[2..] : keyName;
    }

    /// <summary>
    /// Returns the position of a key in the display order: modifiers first, in their order, then every other key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The modifier's index, or the number of modifiers for any other key.</returns>
    public int OrderOf(KeyCode key)
    {
        return Array.IndexOf(_modifierOrder, key) is var index and >= 0 ? index : _modifierOrder.Length;
    }
}
