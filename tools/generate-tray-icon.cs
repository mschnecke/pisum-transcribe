#:package Svg
#:property TargetFramework=net10.0-windows
#:property PublishAot=false

// Renders the app icon src/Pisum.Transcribe/Tray/TrayIcon.svg into TrayIcon.ico next to it. Run it from the repository
// root after editing the SVG: dotnet run tools/generate-tray-icon.cs

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Svg;

const string SvgPath = "src/Pisum.Transcribe/Tray/TrayIcon.svg";
const string IcoPath = "src/Pisum.Transcribe/Tray/TrayIcon.ico";

// The notification area at 100 % to 200 % scaling, and the sizes that Explorer shows.
int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];

var document = SvgDocument.Open(SvgPath);
var frames = sizes.Select(size => Render(document, size)).ToList();

using (var writer = new BinaryWriter(File.Create(IcoPath)))
{
    // ICONDIR, one ICONDIRENTRY per frame, then the frames.
    writer.Write((ushort) 0);
    writer.Write((ushort) 1);
    writer.Write((ushort) sizes.Length);

    var offset = 6 + 16 * sizes.Length;
    for (var i = 0; i < sizes.Length; i++)
    {
        // A width and height of 0 mean 256.
        writer.Write((byte) (sizes[i] % 256));
        writer.Write((byte) (sizes[i] % 256));
        writer.Write((byte) 0);
        writer.Write((byte) 0);
        writer.Write((ushort) 1);
        writer.Write((ushort) 32);
        writer.Write(frames[i].Length);
        writer.Write(offset);
        offset += frames[i].Length;
    }

    foreach (var frame in frames)
    {
        writer.Write(frame);
    }
}

Console.WriteLine($"Wrote {IcoPath} with {string.Join(", ", sizes)} px.");

static byte[] Render(SvgDocument document, int size)
{
    using var bitmap = document.Draw(size, size);
    using var stream = new MemoryStream();
    if (size == 256)
    {
        // The large frame is a PNG, as in the icons that Windows ships.
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    // The smaller frames are 32-bit DIBs: a BITMAPINFOHEADER whose height counts the colour and the mask rows, the BGRA
    // rows bottom-up, and the AND mask. The alpha channel decides the transparency, so the mask stays empty.
    var maskStride = (size + 31) / 32 * 4;
    using var writer = new BinaryWriter(stream);
    writer.Write(40);
    writer.Write(size);
    writer.Write(size * 2);
    writer.Write((ushort) 1);
    writer.Write((ushort) 32);
    writer.Write(0);
    writer.Write(size * size * 4 + maskStride * size);
    writer.Write(0);
    writer.Write(0);
    writer.Write(0);
    writer.Write(0);

    var data = bitmap.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    try
    {
        var row = new byte[size * 4];
        for (var y = size - 1; y >= 0; y--)
        {
            Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
            writer.Write(row);
        }
    }
    finally
    {
        bitmap.UnlockBits(data);
    }

    writer.Write(new byte[maskStride * size]);
    writer.Flush();
    return stream.ToArray();
}
