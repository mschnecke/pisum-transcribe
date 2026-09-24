namespace Pisum.Transcribe.Recording;

/// <summary>
/// The hook's access on Windows, which needs no grant and can't be revoked.
/// </summary>
internal sealed class WindowsHookAccess : IHookAccess
{
    /// <inheritdoc />
    public bool IsAllowed => true;

    /// <inheritdoc />
    public void OnRevoked()
    {
    }
}
