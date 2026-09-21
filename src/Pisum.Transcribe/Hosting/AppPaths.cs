namespace Pisum.Transcribe.Hosting;

/// <summary>
/// The per-user data folders. All data lives under <c>%LOCALAPPDATA%\Pisum Transcribe\</c>,
/// because models and hardware choices are machine-specific and must not roam.
/// </summary>
internal sealed class AppPaths
{
    /// <summary>
    /// Initializes a new instance rooted at <c>%LOCALAPPDATA%\Pisum Transcribe\</c>.
    /// </summary>
    public AppPaths()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Pisum Transcribe"))
    {
    }

    /// <summary>
    /// Initializes a new instance with a custom root, for tests.
    /// </summary>
    /// <param name="root">The root data folder.</param>
    public AppPaths(string root)
    {
        Root = root;
    }

    /// <summary>
    /// The root data folder.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// The folder for the rolling log files.
    /// </summary>
    public string LogsDirectory => Path.Combine(Root, "logs");

    /// <summary>
    /// The folder for downloaded speech models.
    /// </summary>
    public string ModelsDirectory => Path.Combine(Root, "models");

    /// <summary>
    /// Creates the root data folder if it does not exist.
    /// </summary>
    public void EnsureRootExists()
    {
        Directory.CreateDirectory(Root);
    }
}
