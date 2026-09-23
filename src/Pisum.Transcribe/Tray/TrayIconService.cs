using Avalonia.Controls;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.Tray;

/// <summary>
/// The tray icon, built on Avalonia's <see cref="TrayIcon"/> and a <see cref="NativeMenu"/>. It is created in code,
/// because the application has no window to host it.
/// </summary>
/// <remarks>
/// On Windows the menu is Avalonia's own popup, and <see cref="NativeMenu.Opening"/> never runs there. The items are
/// updated when the popup opens instead: each open creates a new popup window of the internal type
/// <see cref="TrayPopupTypeName"/>, and a class handler on <see cref="Window.WindowOpenedEvent"/> runs before its content
/// is loaded, so the items show updated on that open.
/// </remarks>
internal sealed class TrayIconService : ITrayIconService
{
    /// <summary>
    /// The type name of the window that Avalonia's Win32 backend opens for the tray menu.
    /// </summary>
    internal const string TrayPopupTypeName = "TrayPopupRoot";

    private const string ProductName = "Pisum Transcribe";
    private const string IconResourceName = "Pisum.Transcribe.Tray.TrayIcon.ico";
    private const string StatusIconResourcePrefix = "Pisum.Transcribe.Tray.Windows.TrayGlyph.";

    private readonly ITaskbarModeWatcher _taskbarMode;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly TrayIcon _trayIcon;
    private readonly NativeMenu _menu = new();
    private readonly NativeMenuItem _exitItem = new("Exit");
    private readonly NativeMenuItemSeparator _exitSeparator = new();
    private readonly List<(NativeMenuItem Item, Func<string> Header, Func<bool>? IsVisible)> _menuItems = [];
    private readonly IDisposable _menuOpenedHandler;
    private TrayStatus? _status;
    private bool _removed;

    /// <summary>
    /// Initializes a new instance with the app icon and a menu with <b>Exit</b>, and loads the status icons. The icon
    /// is not shown yet.
    /// </summary>
    /// <param name="taskbarMode">The taskbar's mode, which the icon at rest follows.</param>
    /// <param name="uiDispatcher">Moves a change of the taskbar's mode to the UI thread.</param>
    public TrayIconService(ITaskbarModeWatcher taskbarMode, IUiDispatcher uiDispatcher)
    {
        _taskbarMode = taskbarMode;
        _uiDispatcher = uiDispatcher;
        Icons = LoadIcons();
        _taskbarMode.Changed += OnTaskbarModeChanged;
        _exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(_exitItem);

        _trayIcon = new TrayIcon
        {
            Icon = LoadIcon(IconResourceName),
            ToolTipText = ProductName,
            Menu = _menu,
            IsVisible = false,
        };
        _trayIcon.Clicked += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
        _menuOpenedHandler = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            if (window.GetType().Name == TrayPopupTypeName)
            {
                UpdateMenuItems();
            }
        });
    }

    /// <inheritdoc />
    public event EventHandler? ExitRequested;

    /// <inheritdoc />
    public event EventHandler? Clicked;

    /// <summary>
    /// The menu, for tests.
    /// </summary>
    internal NativeMenu Menu => _menu;

    /// <summary>
    /// The status icons, for tests.
    /// </summary>
    internal TrayIcons Icons { get; }

    /// <summary>
    /// The status icon that the tray shows, or <see langword="null"/> before the first status, for tests.
    /// </summary>
    internal WindowIcon? ShownIcon { get; private set; }

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
        ApplyIcon(status);
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
        _taskbarMode.Changed -= OnTaskbarModeChanged;
        _menuOpenedHandler.Dispose();
        _trayIcon.Dispose();
    }

    /// <summary>
    /// The icon of a status on a taskbar.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <param name="mode">The taskbar's mode.</param>
    /// <param name="icons">The status icons.</param>
    /// <returns>One of <paramref name="icons"/>.</returns>
    internal static WindowIcon IconFor(TrayStatus status, TaskbarMode mode, TrayIcons icons)
    {
        return (status, mode) switch
        {
            (TrayStatus.Ready, TaskbarMode.Light) => icons.ReadyLight,
            (TrayStatus.Ready, TaskbarMode.Dark) => icons.ReadyDark,
            (TrayStatus.Unavailable, TaskbarMode.Light) => icons.UnavailableLight,
            (TrayStatus.Unavailable, TaskbarMode.Dark) => icons.UnavailableDark,
            (TrayStatus.Recording, _) => icons.Recording,
            (TrayStatus.Transcribing, _) => icons.Transcribing,
            _ => throw new ArgumentOutOfRangeException(nameof(status), (status, mode), null),
        };
    }

    /// <summary>
    /// Loads the status icons from the embedded ICOs. Avalonia hands the tray the frame at the small icon size of the
    /// display's scaling (<c>SM_CXSMICON</c>).
    /// </summary>
    /// <returns>The status icons.</returns>
    internal static TrayIcons LoadIcons()
    {
        return new TrayIcons(
            LoadStatusIcon("Ready.Light"),
            LoadStatusIcon("Ready.Dark"),
            LoadStatusIcon("Unavailable.Light"),
            LoadStatusIcon("Unavailable.Dark"),
            LoadStatusIcon("Recording"),
            LoadStatusIcon("Transcribing"));

        static WindowIcon LoadStatusIcon(string name)
        {
            return LoadIcon($"{StatusIconResourcePrefix}{name}.ico");
        }
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

    private static WindowIcon LoadIcon(string resourceName)
    {
        using var stream = typeof(TrayIconService).Assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Embedded resource {resourceName} is missing.");
        return new WindowIcon(stream);
    }

    private void OnTaskbarModeChanged(object? sender, EventArgs e)
    {
        // Raised on the watcher's thread.
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            if (!_removed && _status is { } status)
            {
                ApplyIcon(status);
            }
        });
    }

    private void ApplyIcon(TrayStatus status)
    {
        ShownIcon = IconFor(status, _taskbarMode.Current, Icons);
        _trayIcon.Icon = ShownIcon;
    }
}
