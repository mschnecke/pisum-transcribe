using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Permissions;

namespace Pisum.Transcribe.Recording;

/// <summary>
/// The hook's access on macOS (design D3 of add-macos-recording): the hook may start only while the Accessibility grant
/// is in effect, and a revoke is reported to <see cref="IPermissions"/>, so a new grant restarts the application.
/// </summary>
internal sealed class MacHookAccess : IHookAccess
{
    private readonly IPermissions _permissions;
    private readonly IUiDispatcher _uiDispatcher;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="permissions">Holds whether the Accessibility grant is in effect.</param>
    /// <param name="uiDispatcher">Reaches the UI thread, where <see cref="IPermissions"/> is used.</param>
    public MacHookAccess(IPermissions permissions, IUiDispatcher uiDispatcher)
    {
        _permissions = permissions;
        _uiDispatcher = uiDispatcher;
    }

    /// <inheritdoc />
    public bool IsAllowed => _permissions.IsAccessibilityInEffect;

    /// <inheritdoc />
    public void OnRevoked()
    {
        _ = _uiDispatcher.InvokeAsync(_permissions.OnAccessibilityRevoked);
    }
}
