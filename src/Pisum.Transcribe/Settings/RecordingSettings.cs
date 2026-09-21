using Pisum.Transcribe.Recording;

namespace Pisum.Transcribe.Settings;

/// <summary>
/// The recording settings, saved as the <c>recording</c> section.
/// </summary>
internal sealed record RecordingSettings
{
    /// <summary>
    /// The push-to-talk hotkey as SharpHook <c>KeyCode</c> names, such as <c>VcRightControl</c>. Names, not numeric codes,
    /// because SharpHook renumbers key codes between major versions. Parse it with <see cref="HotkeyParser.Parse"/>, which
    /// replaces a missing, empty or unknown value by the default.
    /// </summary>
    public IReadOnlyList<string> Hotkey { get; init; } = [HotkeyParser.DefaultKeyName];

    /// <summary>
    /// Compares the hotkeys element by element, so that loaded settings equal the defaults they match.
    /// </summary>
    /// <param name="other">The settings to compare with.</param>
    /// <returns><see langword="true"/> if both hold the same key names in the same order.</returns>
    public bool Equals(RecordingSettings? other)
    {
        // Hotkey is null when the file says so, despite the non-nullable type.
        return other is not null &&
               (ReferenceEquals(Hotkey, other.Hotkey) ||
                (Hotkey is not null && other.Hotkey is not null && Hotkey.SequenceEqual(other.Hotkey)));
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var name in Hotkey ?? [])
        {
            hash.Add(name);
        }

        return hash.ToHashCode();
    }
}
