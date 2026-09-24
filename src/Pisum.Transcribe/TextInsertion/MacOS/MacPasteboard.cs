using System.Runtime.InteropServices;
using Pisum.Transcribe.Hosting;

namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// A pasteboard through the helper's pasteboard functions (design D3 of add-macos-text-insertion).
/// </summary>
internal sealed class MacPasteboard : IMacPasteboard
{
    private readonly string? _name;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="name">The pasteboard's name, or <see langword="null"/> for the general pasteboard.</param>
    public MacPasteboard(string? name)
    {
        _name = name;
    }

    /// <inheritdoc />
    public long ChangeCount => PisumMac.PasteboardChangeCount(_name);

    /// <inheritdoc />
    public int AccessBehavior => _name is null ? PisumMac.PasteboardAccessBehavior() : -1;

    /// <inheritdoc />
    public byte[]? Snapshot()
    {
        if (PisumMac.PasteboardSnapshot(_name, out var buffer, out var length) != 0)
        {
            return null;
        }

        try
        {
            var bytes = new byte[length];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            PisumMac.Free(buffer);
        }
    }

    /// <inheritdoc />
    public bool SetText(string text, bool exclude)
    {
        return PisumMac.PasteboardSetText(_name, text, exclude ? 1 : 0) == 0;
    }

    /// <inheritdoc />
    public bool Restore(byte[] buffer)
    {
        return PisumMac.PasteboardRestore(_name, buffer, buffer.Length, 1) == 0;
    }
}
