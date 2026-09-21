using System.Drawing;

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
    /// Raised on the UI thread when the user double-clicks the icon.
    /// </summary>
    event EventHandler? DoubleClicked;

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
    /// Changes the icon and its tooltip.
    /// </summary>
    /// <param name="icon">The new icon. The tray shows a copy, so the caller keeps ownership of it.</param>
    /// <param name="toolTip">The new tooltip.</param>
    void SetStatus(Icon icon, string toolTip);

    /// <summary>
    /// Shows a notification from the tray icon.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification text.</param>
    void ShowNotification(string title, string message);

    /// <summary>
    /// Removes the icon from the notification area. Later calls have no effect.
    /// </summary>
    void Remove();
}
