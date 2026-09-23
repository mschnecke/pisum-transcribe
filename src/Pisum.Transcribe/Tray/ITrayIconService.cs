namespace Pisum.Transcribe.Tray;

/// <summary>
/// The notification-area (tray) icon and its context menu. Call all members on the UI thread.
/// </summary>
internal interface ITrayIconService
{
    /// <summary>
    /// Raised when the user chooses <b>Exit</b> in the context menu.
    /// </summary>
    event EventHandler? ExitRequested;

    /// <summary>
    /// Raised on the UI thread when the user clicks the icon with the left button, on the release. A double-click
    /// raises it twice. A right click opens the menu and doesn't raise it.
    /// </summary>
    event EventHandler? Clicked;

    /// <summary>
    /// Shows the icon in the notification area.
    /// </summary>
    void Show();

    /// <summary>
    /// Adds a menu item above <b>Exit</b>.
    /// </summary>
    /// <param name="header">The text of the menu item.</param>
    /// <param name="onClick">Runs when the user chooses the menu item.</param>
    /// <param name="isVisible">
    /// Runs on the UI thread each time the menu opens and decides whether the item is shown. Keep it cheap.
    /// <see langword="null"/> shows the item always.
    /// </param>
    void AddMenuItem(string header, Action onClick, Func<bool>? isVisible = null);

    /// <summary>
    /// Adds a menu item above <b>Exit</b> whose text is read each time the menu opens.
    /// </summary>
    /// <param name="header">
    /// Runs on the UI thread each time the menu opens, while the item is shown, and returns its text. Keep it cheap.
    /// </param>
    /// <param name="onClick">Runs when the user chooses the menu item.</param>
    /// <param name="isVisible">
    /// Runs on the UI thread each time the menu opens and decides whether the item is shown. Keep it cheap.
    /// <see langword="null"/> shows the item always.
    /// </param>
    void AddMenuItem(Func<string> header, Action onClick, Func<bool>? isVisible = null);

    /// <summary>
    /// Changes the icon to the one of a status, and its tooltip.
    /// </summary>
    /// <param name="status">The status the icon shows.</param>
    /// <param name="toolTip">The new tooltip.</param>
    void SetStatus(TrayStatus status, string toolTip);

    /// <summary>
    /// Removes the icon from the notification area. Later calls have no effect.
    /// </summary>
    void Remove();
}
