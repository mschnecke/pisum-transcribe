using Avalonia.Controls;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tray;

/// <summary>
/// The tray icon, built on Avalonia's <see cref="TrayIcon"/> and a <see cref="NativeMenu"/>. It is created in code,
/// because the application has no window to host it.
/// </summary>
/// <remarks>
/// <para>
/// On Windows the menu is Avalonia's own popup, and <see cref="NativeMenu.Opening"/> never runs there. The items are
/// updated when the popup opens instead: each open creates a new popup window of the internal type
/// <see cref="TrayPopupTypeName"/>, and a class handler on <see cref="Window.WindowOpenedEvent"/> runs before its content
/// is loaded, so the items show updated on that open.
/// </para>
/// <para>
/// On macOS the menu is a native menu, which raises <see cref="NativeMenu.Opening"/> before each open, and a click on the
/// icon opens it, so <see cref="Clicked"/> is never raised there.
/// </para>
/// </remarks>
internal sealed class TrayIconService : ITrayIconService
{
    /// <summary>
    /// The type name of the window that Avalonia's Win32 backend opens for the tray menu.
    /// </summary>
    internal const string TrayPopupTypeName = "TrayPopupRoot";

    /// <summary>
    /// The last menu item, which ends the application: <b>Exit</b> on Windows, and <b>Quit Pisum Transcribe</b> on
    /// macOS, as Apple's guidelines name it.
    /// </summary>
#if WINDOWS
    internal const string ExitHeader = "Exit";
#else
    internal const string ExitHeader = "Quit Pisum Transcribe";
#endif

    private const string ProductName = "Pisum Transcribe";

    private readonly ITrayIconSet _icons;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly TrayIcon _trayIcon;
    private readonly NativeMenu _menu = new();
    private readonly NativeMenuItem _exitItem = new(ExitHeader);
    private readonly NativeMenuItemSeparator _exitSeparator = new();
    private readonly List<(NativeMenuItem Item, Func<string> Header, Func<bool>? IsVisible)> _menuItems = [];
#if WINDOWS
    private readonly IDisposable _menuOpenedHandler;
#endif
    private TrayStatus? _status;
    private bool _removed;

    /// <summary>
    /// Initializes a new instance with the platform's initial icon and a menu with <see cref="ExitHeader"/>. The icon is
    /// not shown yet.
    /// </summary>
    /// <param name="icons">The platform's icons.</param>
    /// <param name="uiDispatcher">Moves a change of the icons to the UI thread.</param>
    public TrayIconService(ITrayIconSet icons, IUiDispatcher uiDispatcher)
    {
        _icons = icons;
        _uiDispatcher = uiDispatcher;
        _icons.Changed += OnIconsChanged;
        _exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(_exitItem);

        _trayIcon = new TrayIcon
        {
            ToolTipText = ProductName,
            Menu = _menu,
            IsVisible = false,
        };
        Apply(_icons.Initial);
#if WINDOWS
        _trayIcon.Clicked += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
        _menuOpenedHandler = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            if (window.GetType().Name == TrayPopupTypeName)
            {
                UpdateMenuItems();
            }
        });
#else
        // Runs on every open, after NeedsUpdate, and shows the changes on that open (spike M1 of
        // move-windows-shell-to-avalonia).
        _menu.Opening += (_, _) => UpdateMenuItems();
#endif
    }

    /// <inheritdoc />
    public event EventHandler? ExitRequested;

#if WINDOWS
    /// <inheritdoc />
    public event EventHandler? Clicked;
#else
    /// <inheritdoc />
    /// <remarks>Never raised on macOS, where a click on the icon opens the menu.</remarks>
    public event EventHandler? Clicked
    {
        add { }
        remove { }
    }
#endif

    /// <summary>
    /// The menu, for tests.
    /// </summary>
    internal NativeMenu Menu => _menu;

    /// <summary>
    /// The icon that the tray shows, for tests.
    /// </summary>
    internal TrayIconImage ShownIcon { get; private set; } = null!;

    /// <summary>
    /// Whether the tray draws the icon as a template image, for tests.
    /// </summary>
    internal bool IsTemplateIcon => MacOSProperties.GetIsTemplateIcon(_trayIcon);

    /// <summary>
    /// The tooltip that the tray shows, for tests.
    /// </summary>
    internal string? ToolTip => _trayIcon.ToolTipText;

    /// <summary>
    /// Whether the icon is shown in the notification area, for tests.
    /// </summary>
    internal bool IsShown => !_removed && _trayIcon.IsVisible;

    /// <inheritdoc />
    public void Show()
    {
        if (!_removed)
        {
            _trayIcon.IsVisible = true;
        }
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
        var item = new NativeMenuItem();
        item.Click += (_, _) => onClick();
        _menuItems.Add((item, header, isVisible));

        if (_menu.Items.IndexOf(_exitItem) == 0)
        {
            _menu.Items.Insert(0, _exitSeparator);
        }

        // Above the separator that precedes Exit.
        _menu.Items.Insert(_menu.Items.IndexOf(_exitItem) - 1, item);
    }

    /// <inheritdoc />
    public void SetStatus(TrayStatus status, string toolTip)
    {
        if (_removed)
        {
            return;
        }

        _status = status;
        Apply(_icons.For(status));
        _trayIcon.ToolTipText = toolTip;
    }

    /// <inheritdoc />
    public void Remove()
    {
        if (_removed)
        {
            return;
        }

        _removed = true;
        _icons.Changed -= OnIconsChanged;
#if WINDOWS
        _menuOpenedHandler.Dispose();
#endif
        _trayIcon.Dispose();
    }

    /// <summary>
    /// Shows or hides each added item and sets the text of the shown ones. Runs when the menu opens.
    /// </summary>
    internal void UpdateMenuItems()
    {
        var anyVisible = false;
        foreach (var (item, header, isVisible) in _menuItems)
        {
            var visible = isVisible?.Invoke() ?? true;
            if (visible)
            {
                item.Header = header();
            }

            item.IsVisible = visible;
            anyVisible |= visible;
        }

        _exitSeparator.IsVisible = anyVisible;
    }

    private void OnIconsChanged(object? sender, EventArgs e)
    {
        // Raised on any thread.
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            if (!_removed && _status is { } status)
            {
                Apply(_icons.For(status));
            }
        });
    }

    private void Apply(TrayIconImage icon)
    {
        // Together, so macOS never draws a colored icon as a template or the other way round.
        ShownIcon = icon;
        MacOSProperties.SetIsTemplateIcon(_trayIcon, icon.IsTemplate);
        _trayIcon.Icon = icon.Icon;
    }
}
