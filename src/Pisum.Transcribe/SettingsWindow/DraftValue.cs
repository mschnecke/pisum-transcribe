namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// Moves a draft value to newly saved settings.
/// </summary>
internal static class DraftValue
{
    /// <summary>
    /// Takes the newly saved value, unless the user edited the draft value.
    /// </summary>
    /// <param name="draft">The value in the window.</param>
    /// <param name="previous">The saved value the draft was based on.</param>
    /// <param name="current">The newly saved value.</param>
    /// <typeparam name="T">The value type.</typeparam>
    /// <returns><paramref name="current"/> if the draft was not edited, otherwise <paramref name="draft"/>.</returns>
    public static T Rebase<T>(T draft, T previous, T current)
    {
        return EqualityComparer<T>.Default.Equals(draft, previous) ? current : draft;
    }
}
