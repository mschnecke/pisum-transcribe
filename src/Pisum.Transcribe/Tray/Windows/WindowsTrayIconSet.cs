using Avalonia.Controls;

namespace Pisum.Transcribe.Tray;

/// <summary>
/// The tray icons on Windows: the app icon before the first status, and the status icons from the ICOs in
/// <c>Tray/Windows</c>, whose icon at rest follows the taskbar's mode.
/// </summary>
internal sealed class WindowsTrayIconSet : ITrayIconSet
{
    private const string IconResourceName = "Pisum.Transcribe.Tray.TrayIcon.ico";
    private const string StatusIconResourcePrefix = "Pisum.Transcribe.Tray.Windows.TrayGlyph.";

    private readonly ITaskbarModeWatcher _taskbarMode;

    /// <summary>
    /// Initializes a new instance and loads the icons.
    /// </summary>
    /// <param name="taskbarMode">The taskbar's mode, which the icon at rest follows.</param>
    public WindowsTrayIconSet(ITaskbarModeWatcher taskbarMode)
    {
        _taskbarMode = taskbarMode;
        Initial = new TrayIconImage(LoadIcon(IconResourceName), false);
        Icons = LoadIcons();
    }

    /// <inheritdoc />
    public event EventHandler? Changed
    {
        add => _taskbarMode.Changed += value;
        remove => _taskbarMode.Changed -= value;
    }

    /// <inheritdoc />
    public TrayIconImage Initial { get; }

    /// <summary>
    /// The status icons, for tests.
    /// </summary>
    internal TrayIcons Icons { get; }

    /// <inheritdoc />
    public TrayIconImage For(TrayStatus status)
    {
        return new TrayIconImage(IconFor(status, _taskbarMode.Current, Icons), false);
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

    private static WindowIcon LoadIcon(string resourceName)
    {
        using var stream = typeof(WindowsTrayIconSet).Assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Embedded resource {resourceName} is missing.");
        return new WindowIcon(stream);
    }
}
