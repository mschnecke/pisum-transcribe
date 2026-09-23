using Microsoft.Extensions.Logging;

namespace Pisum.Transcribe.Hosting;

/// <summary>
/// Tells from the quit Apple event why macOS asks the application to quit. Avalonia raises the lifetime's
/// <c>ShutdownRequested</c> for it, and the event carries no reason at a real logout, but its sender is
/// <c>loginwindow</c> then (design D5 of add-macos-shell).
/// </summary>
internal sealed class QuitEventSender
{
    /// <summary>
    /// The process that sends the quit event when the macOS session ends: at a logout, a shutdown or a restart.
    /// </summary>
    public const string SessionEndSender = "loginwindow";

    private readonly Func<int> _readSenderPid;
    private readonly Func<int, string?> _readProcessName;
    private readonly ILogger<QuitEventSender> _logger;

    /// <summary>
    /// Initializes a new instance that reads the event through the Swift helper.
    /// </summary>
    /// <param name="library">The Swift helper.</param>
    /// <param name="logger">The logger.</param>
    public QuitEventSender(MacNativeLibrary library, ILogger<QuitEventSender> logger)
        : this(() => library.IsAvailable ? PisumMac.CurrentQuitSenderPid() : 0, PisumMac.ProcessName, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance, for tests.
    /// </summary>
    /// <param name="readSenderPid">Reads the process ID of the current event's sender, or 0.</param>
    /// <param name="readProcessName">Reads a process's name, or <see langword="null"/>.</param>
    /// <param name="logger">The logger.</param>
    public QuitEventSender(Func<int> readSenderPid, Func<int, string?> readProcessName, ILogger<QuitEventSender> logger)
    {
        _readSenderPid = readSenderPid;
        _readProcessName = readProcessName;
        _logger = logger;
    }

    /// <summary>
    /// Reads the sender of the quit event being handled and tells the reason: <see cref="ShutdownReason.SessionEnd"/>
    /// when it's <see cref="SessionEndSender"/>, and <see cref="ShutdownReason.UserExit"/> for any other sender or none.
    /// Call it on the UI thread while the event is handled.
    /// </summary>
    /// <returns>The reason.</returns>
    public ShutdownReason ReadReason()
    {
        var pid = _readSenderPid();
        var sender = pid == 0 ? null : _readProcessName(pid);
        _logger.LogInformation("Quit requested by {Sender}", sender ?? "an unknown sender");
        return sender == SessionEndSender ? ShutdownReason.SessionEnd : ShutdownReason.UserExit;
    }
}
