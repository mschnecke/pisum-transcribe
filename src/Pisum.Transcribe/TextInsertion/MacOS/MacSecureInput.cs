using System.Runtime.InteropServices;

namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// Secure Event Input through Carbon's <c>IsSecureEventInputEnabled</c> (design D5 of add-macos-text-insertion), a
/// cheap call that worked from any thread on macOS 27.0. It is system-wide: any application that turned it on counts.
/// </summary>
internal sealed class MacSecureInput : ISecureInput
{
    private const string CarbonPath = "/System/Library/Frameworks/Carbon.framework/Carbon";

    /// <inheritdoc />
    public bool IsEnabled => IsSecureEventInputEnabled();

    [DllImport(CarbonPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool IsSecureEventInputEnabled();
}
