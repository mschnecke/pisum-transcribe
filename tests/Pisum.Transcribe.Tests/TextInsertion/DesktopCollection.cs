namespace Pisum.Transcribe.Tests.TextInsertion;

/// <summary>
/// Tests that use the shared desktop: the system clipboard, the foreground window or simulated keystrokes. They run one
/// at a time and alone, so they neither take the foreground from each other nor overwrite each other's clipboard.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DesktopCollection
{
    public const string Name = "Desktop";
}
