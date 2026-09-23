using System.Drawing;
using System.Windows.Controls;
using H.NotifyIcon;
using Pisum.Transcribe.Hosting;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Pisum.Transcribe.Tray;

/// <summary>
/// The tray icon, built on H.NotifyIcon. It is created in code, because the application has no window to host it.
/// </summary>
internal sealed class TrayIconService : ITrayIconService
{
    private const string ProductName = "Pisum Transcribe";
    private const string IconResourceName = "Pisum.Transcribe.Tray.TrayIcon.ico";
    private const string StatusIconResourcePrefix = "Pisum.Transcribe.Tray.Windows.TrayGlyph.";

    private readonly ITaskbarModeWatcher _taskbarMode;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly TaskbarIcon _taskbarIcon;
    private readonly ContextMenu _contextMenu = new();
    private readonly MenuItem _exitItem = new() {Header = "Exit"};
    private readonly Separator _exitSeparator = new();
    private readonly List<(MenuItem Item, Func<string> Header, Func<bool>? IsVisible)> _menuItems = [];
    private TrayStatus? _status;
    private bool _removed;

    /// <summary>
    /// Initializes a new instance with the app icon and a context menu with <b>Exit</b>, and loads the status icons.
    /// The icon is not shown yet.
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

    /// <summary>
    /// The status icons, for tests.
    /// </summary>
    internal TrayIcons Icons { get; }

    /// <summary>
    /// The status icon that the tray shows a copy of, or <see langword="null"/> before the first status, for tests.
    /// </summary>
    internal Icon? ShownIcon { get; private set; }

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
    public void SetStatus(TrayStatus status, string toolTip)
    {
        _status = status;
        ApplyIcon(status);
        _taskbarIcon.ToolTipText = toolTip;
    }

    /// <inheritdoc />
    public void Remove()
    {
        _removed = true;
        _taskbarMode.Changed -= OnTaskbarModeChanged;
        _taskbarIcon.Dispose();
    }

    /// <summary>
    /// The icon of a status on a taskbar.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <param name="mode">The taskbar's mode.</param>
    /// <param name="icons">The status icons.</param>
    /// <returns>One of <paramref name="icons"/>, which the caller must not dispose.</returns>
    internal static Icon IconFor(TrayStatus status, TaskbarMode mode, TrayIcons icons)
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
    /// Loads the status icons from the embedded ICOs, each at the small icon size of the primary display's scaling
    /// (<c>SM_CXSMICON</c>, as <c>SystemInformation.SmallIconSize</c> reads it). H.NotifyIcon hands the icon to the tray
    /// as it is, so the frame chosen here is the one that Windows shows.
    /// </summary>
    /// <returns>The status icons.</returns>
    internal static TrayIcons LoadIcons()
    {
        var size = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON);
        return new TrayIcons(
            Load("Ready.Light"),
            Load("Ready.Dark"),
            Load("Unavailable.Light"),
            Load("Unavailable.Dark"),
            Load("Recording"),
            Load("Transcribing"));

        Icon Load(string name)
        {
            var resourceName = $"{StatusIconResourcePrefix}{name}.ico";
            using var stream = typeof(TrayIconService).Assembly.GetManifestResourceStream(resourceName)
                               ?? throw new InvalidOperationException($"Embedded resource {resourceName} is missing.");
            return new Icon(stream, size, size);
        }
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

        // H.NotifyIcon disposes the icon it replaces and the icon it holds when it is disposed, so it gets a copy.
        _taskbarIcon.Icon = (Icon) ShownIcon.Clone();
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
