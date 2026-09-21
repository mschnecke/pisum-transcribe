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
    /// The Windows session ends: sign-out, shutdown or restart. Exit code 0.
    /// </summary>
    SessionEnd,
}
