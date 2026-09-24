namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// Windows has no secure input that hides keystrokes from other applications; elevated windows are checked instead.
/// </summary>
internal sealed class NoSecureInput : ISecureInput
{
    /// <inheritdoc />
    public bool IsEnabled => false;
}
