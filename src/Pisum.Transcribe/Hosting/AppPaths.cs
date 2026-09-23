namespace Pisum.Transcribe.Hosting;

/// <summary>
/// The per-user data folders. All data lives under <c>%LOCALAPPDATA%\Pisum Transcribe\</c> on Windows and under
/// <c>~/Library/Application Support/Pisum Transcribe/</c> on macOS, because models and hardware choices are
/// machine-specific and must not roam. The logs are the exception on macOS: they go to
/// <c>~/Library/Logs/Pisum Transcribe/</c>, where Console.app shows them.
/// </summary>
internal sealed class AppPaths
{
    private const string FolderName = "Pisum Transcribe";

    private static readonly string DefaultRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);

    /// <summary>
    /// Initializes a new instance rooted at the platform's local application data folder.
    /// </summary>
    public AppPaths()
#if WINDOWS
        : this(DefaultRoot)
#else
        : this(DefaultRoot,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", FolderName))
#endif
    {
    }

    /// <summary>
    /// Initializes a new instance with a custom root and the logs under it, for tests.
    /// </summary>
    /// <param name="root">The root data folder.</param>
    public AppPaths(string root)
        : this(root, Path.Combine(root, "logs"))
    {
    }

    /// <summary>
    /// Initializes a new instance with a custom root and logs folder, for tests.
    /// </summary>
    /// <param name="root">The root data folder.</param>
    /// <param name="logsDirectory">The folder for the rolling log files.</param>
    public AppPaths(string root, string logsDirectory)
    {
        Root = root;
        LogsDirectory = logsDirectory;
    }

    /// <summary>
    /// The root data folder.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// The folder for the rolling log files: <c>logs</c> under <see cref="Root"/> on Windows, and
    /// <c>~/Library/Logs/Pisum Transcribe/</c> on macOS.
    /// </summary>
    public string LogsDirectory { get; }

    /// <summary>
    /// The folder for downloaded speech models.
    /// </summary>
    public string ModelsDirectory => Path.Combine(Root, "models");

    /// <summary>
    /// Creates the root data folder and the logs folder if they do not exist. The logs folder lies outside the root on
    /// macOS.
    /// </summary>
    public void EnsureRootExists()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
    }
}
