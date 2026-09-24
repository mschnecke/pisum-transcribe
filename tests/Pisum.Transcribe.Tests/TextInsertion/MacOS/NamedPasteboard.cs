using System.Runtime.InteropServices;
using System.Text;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.TextInsertion;

namespace Pisum.Transcribe.Tests.TextInsertion;

/// <summary>
/// A pasteboard of the test's own, through the real Swift helper, so that tests leave the user's clipboard alone and
/// never meet pasteboard privacy. Released on dispose.
/// </summary>
internal sealed class NamedPasteboard : IDisposable
{
    public const string TextType = "public.utf8-plain-text";

    public string Name { get; } = $"io.github.mschnecke.pisum-transcribe.tests.{Guid.NewGuid():N}";

    public long ChangeCount => PisumMac.PasteboardChangeCount(Name);

    public static MacPasteboardEntry Text(string text)
    {
        return new MacPasteboardEntry(TextType, Encoding.UTF8.GetBytes(text));
    }

    public static MacPasteboardEntry Marker(string type)
    {
        return new MacPasteboardEntry(type, []);
    }

    /// <summary>
    /// Reads every item with its types through <c>pisum_pasteboard_snapshot</c>.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<MacPasteboardEntry>> Read()
    {
        PisumMac.PasteboardSnapshot(Name, out var buffer, out var length).ShouldBe(0);
        try
        {
            var bytes = new byte[length];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            return MacPasteboardFormat.Read(bytes).ShouldNotBeNull();
        }
        finally
        {
            PisumMac.Free(buffer);
        }
    }

    /// <summary>
    /// Replaces the contents with the items as they are, without the markers of a restore.
    /// </summary>
    public void Write(params IReadOnlyList<MacPasteboardEntry>[] items)
    {
        var buffer = MacPasteboardFormat.Write(items);
        PisumMac.PasteboardRestore(Name, buffer, buffer.Length, 0).ShouldBe(0);
    }

    public void Dispose()
    {
        PisumMac.PasteboardRelease(Name);
    }
}
