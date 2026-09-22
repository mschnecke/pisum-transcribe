using System.Drawing;
using System.Windows.Controls;
using H.NotifyIcon;

namespace Pisum.Transcribe.Tray;

/// <summary>
/// The tray icon, built on H.NotifyIcon. It is created in code, because the application has no window to host it.
/// </summary>
internal sealed class TrayIconService : ITrayIconService
{
    private const string ProductName = "Pisum Transcribe";
    private const string IconResourceName = "Pisum.Transcribe.Tray.TrayIcon.ico";

    private readonly TaskbarIcon _taskbarIcon;
    private readonly ContextMenu _contextMenu = new();
    private readonly MenuItem _exitItem = new() {Header = "Exit"};
    private readonly Separator _exitSeparator = new();
    private readonly List<(MenuItem Item, Func<string> Header, Func<bool>? IsVisible)> _menuItems = [];

    /// <summary>
    /// Initializes a new instance with the app icon and a context menu with <b>Exit</b>. The icon is not shown yet.
    /// </summary>
    public TrayIconService()
    {
        _exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        _contextMenu.Items.Add(_exitItem);

        using var iconStream = typeof(TrayIconService).Assembly.GetManifestResourceStream(IconResourceName)
                               ?? throw new InvalidOperationException(
                                   $"Embedded resource {IconResourceName} is missing.");
        _taskbarIcon = new TaskbarIcon
        {
            Icon = new Icon(iconStream),
            ToolTipText = ProductName,
            ContextMenu = _contextMenu,
        };
        _taskbarIcon.PreviewTrayContextMenuOpen += (_, _) => UpdateMenuItemVisibility();
        _taskbarIcon.TrayMouseDoubleClick += (_, _) => DoubleClicked?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public event EventHandler? ExitRequested;

    /// <inheritdoc />
    public event EventHandler? DoubleClicked;

    /// <summary>
    /// The context menu, for tests.
    /// </summary>
    internal ContextMenu ContextMenu => _contextMenu;

    /// <inheritdoc />
    public void Show()
    {
        // The default enables Efficiency mode for the whole process (EcoQoS, idle priority). That would starve the
        // push-to-talk keyboard hook, which Windows removes when it responds too slowly, and audio capture.
        _taskbarIcon.ForceCreate(false);
    }

    /// <inheritdoc />
    public void AddMenuItem(string header, Action onClick, Func<bool>? isVisible = null)
    {
        AddMenuItem(() => header, onClick, isVisible);
    }

    /// <inheritdoc />
    public void AddMenuItem(Func<string> header, Action onClick, Func<bool>? isVisible = null)
    {
        // The header is set when the menu opens.
        var item = new MenuItem();
        item.Click += (_, _) => onClick();
        _menuItems.Add((item, header, isVisible));

        if (_contextMenu.Items.IndexOf(_exitItem) == 0)
        {
            _contextMenu.Items.Insert(0, _exitSeparator);
        }

        // Above the separator that precedes Exit.
        _contextMenu.Items.Insert(_contextMenu.Items.IndexOf(_exitItem) - 1, item);
    }

    /// <inheritdoc />
    public void SetStatus(Icon icon, string toolTip)
    {
        // H.NotifyIcon disposes the icon it replaces and the icon it holds when it is disposed, so it gets a copy.
        _taskbarIcon.Icon = (Icon) icon.Clone();
        _taskbarIcon.ToolTipText = toolTip;
    }

    /// <inheritdoc />
    public void ShowNotification(string title, string message)
    {
        _taskbarIcon.ShowNotification(title, message);
    }

    /// <inheritdoc />
    public void Remove()
    {
        _taskbarIcon.Dispose();
    }

    /// <summary>
    /// Shows or hides each added item and sets the text of the shown ones. Runs when the menu opens.
    /// </summary>
    internal void UpdateMenuItemVisibility()
    {
        var anyVisible = false;
        foreach (var (item, header, isVisible) in _menuItems)
        {
            var visible = isVisible?.Invoke() ?? true;
            if (visible)
            {
                item.Header = header();
            }

            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            anyVisible |= visible;
        }

        _exitSeparator.Visibility = anyVisible ? Visibility.Visible : Visibility.Collapsed;
    }
}
