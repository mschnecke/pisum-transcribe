namespace Pisum.Transcribe.Hosting;

/// <summary>
/// Why the application ends.
/// </summary>
internal enum ShutdownReason
{
    /// <summary>
    /// The user chose <b>Exit</b> in the tray menu. Exit code 0.
    /// </summary>
    UserExit,

    /// <summary>
    /// An unhandled exception on the UI thread or in a background service. Exit code 1.
    /// </summary>
    Error,

    /// <summary>
    /// The Windows or macOS session ends: sign-out, shutdown or restart. Exit code 0.
    /// </summary>
    SessionEnd,

    /// <summary>
    /// A termination request (<c>SIGTERM</c>) on macOS, for example from an installer. Handled like
    /// <see cref="UserExit"/>. Exit code 0.
    /// </summary>
    TerminationRequest,

    /// <summary>
    /// macOS only: Accessibility was granted while the application ran, which the keyboard hook sees only in a new
    /// process. A new instance is started first, and the application then ends like <see cref="UserExit"/>. Exit code 0.
    /// </summary>
    Relaunch,
}
