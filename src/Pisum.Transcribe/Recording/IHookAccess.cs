namespace Pisum.Transcribe.Recording;

/// <summary>
/// Whether the keyboard hook may run, and where a revoke of its access is reported.
/// </summary>
internal interface IHookAccess
{
    /// <summary>
    /// Gets whether the hook may start. On macOS, only with the Accessibility grant in effect.
    /// </summary>
    bool IsAllowed { get; }

    /// <summary>
    /// Reports that the hook ended because its access was revoked while it ran. May be called from any thread.
    /// </summary>
    void OnRevoked();
}
