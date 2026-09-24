using System.Buffers.Binary;
using System.Text;

namespace Pisum.Transcribe.TextInsertion;

/// <summary>
/// One type of a pasteboard item with its data.
/// </summary>
/// <param name="Type">The type's identifier, such as <c>public.utf8-plain-text</c>.</param>
/// <param name="Data">The data.</param>
internal sealed record MacPasteboardEntry(string Type, byte[] Data);

/// <summary>
/// The buffer in which a pasteboard snapshot crosses the helper's ABI (design D3 of add-macos-text-insertion),
/// little-endian: the item count (Int32), then per item its type count (Int32), then per type the name's length (Int32)
/// and UTF-8 bytes, and the data's length (Int64) and bytes.
/// </summary>
internal static class MacPasteboardFormat
{
    /// <summary>
    /// Reads the items of a buffer.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <returns>The items, each a list of its types, or <see langword="null"/> when the buffer is malformed.</returns>
    public static IReadOnlyList<IReadOnlyList<MacPasteboardEntry>>? Read(ReadOnlySpan<byte> buffer)
    {
        var offset = 0;
        if (!TryReadInt32(buffer, ref offset, out var itemCount) || itemCount < 0)
        {
            return null;
        }

        var items = new List<IReadOnlyList<MacPasteboardEntry>>();
        for (var item = 0; item < itemCount; item++)
        {
            if (!TryReadInt32(buffer, ref offset, out var typeCount) || typeCount < 0)
            {
                return null;
            }

            var entries = new List<MacPasteboardEntry>();
            for (var type = 0; type < typeCount; type++)
            {
                if (!TryReadInt32(buffer, ref offset, out var typeLength) ||
                    !TryReadBytes(buffer, ref offset, typeLength, out var typeBytes) ||
                    !TryReadInt64(buffer, ref offset, out var dataLength) ||
                    dataLength > int.MaxValue ||
                    !TryReadBytes(buffer, ref offset, (int) dataLength, out var data))
                {
                    return null;
                }

                entries.Add(new MacPasteboardEntry(Encoding.UTF8.GetString(typeBytes), data.ToArray()));
            }

            items.Add(entries);
        }

        return offset == buffer.Length ? items : null;
    }

    /// <summary>
    /// Writes items into a buffer.
    /// </summary>
    /// <param name="items">The items, each a list of its types.</param>
    /// <returns>The buffer.</returns>
    public static byte[] Write(IReadOnlyList<IReadOnlyList<MacPasteboardEntry>> items)
    {
        using var stream = new MemoryStream();
        Span<byte> number = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt32LittleEndian(number, items.Count);
        stream.Write(number[..sizeof(int)]);
        foreach (var entries in items)
        {
            BinaryPrimitives.WriteInt32LittleEndian(number, entries.Count);
            stream.Write(number[..sizeof(int)]);
            foreach (var entry in entries)
            {
                var type = Encoding.UTF8.GetBytes(entry.Type);
                BinaryPrimitives.WriteInt32LittleEndian(number, type.Length);
                stream.Write(number[..sizeof(int)]);
                stream.Write(type);
                BinaryPrimitives.WriteInt64LittleEndian(number, entry.Data.Length);
                stream.Write(number);
                stream.Write(entry.Data);
            }
        }

        return stream.ToArray();
    }

    private static bool TryReadInt32(ReadOnlySpan<byte> buffer, ref int offset, out int value)
    {
        value = 0;
        if (!TryReadBytes(buffer, ref offset, sizeof(int), out var bytes))
        {
            return false;
        }

        value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        return true;
    }

    private static bool TryReadInt64(ReadOnlySpan<byte> buffer, ref int offset, out long value)
    {
        value = 0;
        if (!TryReadBytes(buffer, ref offset, sizeof(long), out var bytes))
        {
            return false;
        }

        value = BinaryPrimitives.ReadInt64LittleEndian(bytes);
        return true;
    }

    private static bool TryReadBytes(ReadOnlySpan<byte> buffer,
                                     ref int offset,
                                     int count,
                                     out ReadOnlySpan<byte> bytes)
    {
        bytes = default;
        if (count < 0 || count > buffer.Length - offset)
        {
            return false;
        }

        bytes = buffer.Slice(offset, count);
        offset += count;
        return true;
    }
}
