namespace Pisum.Transcribe.Permissions;

/// <summary>
/// The state of a permission.
/// </summary>
internal enum PermissionState
{
    /// <summary>
    /// The user wasn't asked yet, or Accessibility isn't granted.
    /// </summary>
    NotDetermined,

    /// <summary>
    /// Granted, or not needed on this macOS version.
    /// </summary>
    Granted,

    /// <summary>
    /// Denied, or restricted, for example by a device management profile.
    /// </summary>
    Denied,

    /// <summary>
    /// Pasteboard access only: the system asks the user at every read.
    /// </summary>
    AsksEachTime,
}
