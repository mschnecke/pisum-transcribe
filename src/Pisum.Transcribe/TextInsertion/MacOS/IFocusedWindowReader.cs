namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// Reads the focused window of the focused application through the Accessibility API, for
/// <see cref="MacForegroundWindowTracker"/>.
/// </summary>
internal interface IFocusedWindowReader
{
    /// <summary>
    /// Reads the focused application's process and its focused window.
    /// </summary>
    /// <param name="processId">The application's process identifier.</param>
    /// <param name="window">The window's element, retained; release it with <see cref="Release"/>.</param>
    /// <returns><see langword="false"/> when there is no focused window or it could not be read in time.</returns>
    bool TryRead(out int processId, out nint window);

    /// <summary>
    /// Whether two window elements are the same window.
    /// </summary>
    /// <param name="first">The first element.</param>
    /// <param name="second">The second element.</param>
    /// <returns><see langword="true"/> for the same window.</returns>
    bool AreSameWindow(nint first, nint second);

    /// <summary>
    /// Releases an element from <see cref="TryRead"/>.
    /// </summary>
    /// <param name="window">The element.</param>
    void Release(nint window);
}
