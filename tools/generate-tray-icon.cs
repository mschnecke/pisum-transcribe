#:package SkiaSharp
#:package Svg.Skia
#:property TargetFramework=net10.0
#:property PublishAot=false

// Renders the icons of src/Pisum.Transcribe/Tray from its two SVGs. Run it from the repository root after editing
// either of them: dotnet run tools/generate-tray-icon.cs
//
// - TrayIcon.svg, the app icon, into TrayIcon.ico and TrayIcon.png (256 px).
// - TrayGlyph.svg, the status glyph, into the notification area ICOs in Windows/ and the menu bar PNGs in MacOS/.

using System.Buffers.Binary;
using SkiaSharp;
using Svg.Skia;

const string TrayDirectory = "src/Pisum.Transcribe/Tray";

// The notification area at 100 % to 200 % scaling, and the sizes that Explorer shows.
int[] appIconSizes = [16, 20, 24, 32, 40, 48, 64, 256];

// The sizes of the app icon that the notification area uses.
int[] glyphSizes = [16, 20, 24, 32, 40, 48, 64];

var black = SKColors.Black;
var white = SKColors.White;
var red = SKColor.Parse("#E53935");
var amber = SKColor.Parse("#C26A00");

using (var appIcon = LoadSvg($"{TrayDirectory}/TrayIcon.svg"))
{
    WriteIco($"{TrayDirectory}/TrayIcon.ico", appIconSizes.Select(size => Render(appIcon, size, size, null)));
    using var png = Render(appIcon, 256, 256, null);
    WritePng($"{TrayDirectory}/TrayIcon.png", png, null);
}

using (var glyph = LoadSvg($"{TrayDirectory}/TrayGlyph.svg"))
{
    // Light and Dark name the taskbar's mode: a black glyph on a light taskbar, a white one on a dark one.
    Directory.CreateDirectory($"{TrayDirectory}/Windows");
    (string Name, SKColor Color)[] windowsIcons =
    [
        ("Ready.Light", black),
        ("Ready.Dark", white),
        ("Unavailable.Light", Dimmed(black)),
        ("Unavailable.Dark", Dimmed(white)),
        ("Recording", red),
        ("Transcribing", amber),
    ];
    foreach (var (name, color) in windowsIcons)
    {
        WriteIco($"{TrayDirectory}/Windows/TrayGlyph.{name}.ico", glyphSizes.Select(size => Render(glyph, size, size, color)));
    }

    // Apple's menu bar extras: an 18 pt canvas that the glyph's viewBox fills, so that the glyph, inside its padding, is
    // about 16 pt tall and centered. Each size is rendered from the SVG. Ready and unavailable are template images, black
    // with alpha only, which macOS draws in the menu bar's color. The @2x files declare 144 DPI, so that each file
    // reports 18 pt.
    Directory.CreateDirectory($"{TrayDirectory}/MacOS");
    (string Name, SKColor Color)[] macOSIcons =
    [
        ("ReadyTemplate", black),
        ("UnavailableTemplate", Dimmed(black)),
        ("Recording", red),
        ("Transcribing", amber),
    ];
    foreach (var (name, color) in macOSIcons)
    {
        using var standard = Render(glyph, 18, 18, color);
        WritePng($"{TrayDirectory}/MacOS/TrayGlyph.{name}.png", standard, null);
        using var retina = Render(glyph, 36, 36, color);
        WritePng($"{TrayDirectory}/MacOS/TrayGlyph.{name}@2x.png", retina, 144);
    }
}

static SKColor Dimmed(SKColor color) => color.WithAlpha(0x80);

static SKSvg LoadSvg(string path)
{
    var svg = new SKSvg();
    if (svg.Load(path) is null)
    {
        throw new InvalidOperationException($"{path} could not be loaded.");
    }

    return svg;
}

// Renders the SVG into a square of glyphSize pixels, centered on a canvas of canvasSize pixels. A tint replaces the
// color of every pixel and keeps its alpha, multiplied with the tint's alpha. The result is BGRA with straight alpha,
// as ICO and PNG files hold it.
static SKBitmap Render(SKSvg svg, int canvasSize, int glyphSize, SKColor? tint)
{
    var picture = svg.Picture ?? throw new InvalidOperationException("The SVG has no picture.");
    var bounds = picture.CullRect;
    var info = new SKImageInfo(canvasSize, canvasSize, SKColorType.Bgra8888, SKAlphaType.Premul);
    using var surface = SKSurface.Create(info);
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);

    var offset = (canvasSize - glyphSize) / 2f;
    canvas.Translate(offset, offset);
    canvas.Scale(glyphSize / bounds.Width, glyphSize / bounds.Height);
    canvas.Translate(-bounds.Left, -bounds.Top);
    using var paint = new SKPaint();
    if (tint is { } color)
    {
        paint.ColorFilter = SKColorFilter.CreateBlendMode(color, SKBlendMode.SrcIn);
    }

    canvas.DrawPicture(picture, paint);

    var bitmap = new SKBitmap(info.WithAlphaType(SKAlphaType.Unpremul));
    surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);
    return bitmap;
}

static void WriteIco(string path, IEnumerable<SKBitmap> bitmaps)
{
    var frames = bitmaps.Select(bitmap =>
    {
        using (bitmap)
        {
            return (Size: bitmap.Width, Data: IcoFrame(bitmap));
        }
    }).ToList();

    using var writer = new BinaryWriter(File.Create(path));

    // ICONDIR, one ICONDIRENTRY per frame, then the frames.
    writer.Write((ushort) 0);
    writer.Write((ushort) 1);
    writer.Write((ushort) frames.Count);

    var offset = 6 + 16 * frames.Count;
    foreach (var (size, data) in frames)
    {
        // A width and height of 0 mean 256.
        writer.Write((byte) (size % 256));
        writer.Write((byte) (size % 256));
        writer.Write((byte) 0);
        writer.Write((byte) 0);
        writer.Write((ushort) 1);
        writer.Write((ushort) 32);
        writer.Write(data.Length);
        writer.Write(offset);
        offset += data.Length;
    }

    foreach (var (_, data) in frames)
    {
        writer.Write(data);
    }

    Console.WriteLine($"Wrote {path} with {string.Join(", ", frames.Select(frame => frame.Size))} px.");
}

static byte[] IcoFrame(SKBitmap bitmap)
{
    var size = bitmap.Width;
    if (size == 256)
    {
        // The large frame is a PNG, as in the icons that Windows ships.
        return EncodePng(bitmap, null);
    }

    // The smaller frames are 32-bit DIBs: a BITMAPINFOHEADER whose height counts the colour and the mask rows, the BGRA
    // rows bottom-up, and the AND mask. The alpha channel decides the transparency, so the mask stays empty.
    var maskStride = (size + 31) / 32 * 4;
    using var stream = new MemoryStream();
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

    var pixels = bitmap.GetPixelSpan();
    for (var y = size - 1; y >= 0; y--)
    {
        writer.Write(pixels.Slice(y * bitmap.RowBytes, size * 4));
    }

    writer.Write(new byte[maskStride * size]);
    writer.Flush();
    return stream.ToArray();
}

static void WritePng(string path, SKBitmap bitmap, int? dpi)
{
    File.WriteAllBytes(path, EncodePng(bitmap, dpi));
    Console.WriteLine($"Wrote {path} with {bitmap.Width} px{(dpi is null ? "" : $" at {dpi} DPI")}.");
}

// A PNG that declares sRGB and, if given, its DPI. Both chunks go right after IHDR, before the image data.
static byte[] EncodePng(SKBitmap bitmap, int? dpi)
{
    using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
    var png = data.ToArray();

    // The 8-byte signature, then IHDR: 4 bytes length, 4 bytes type, 13 bytes data, 4 bytes CRC.
    const int afterHeader = 8 + 4 + 4 + 13 + 4;
    using var stream = new MemoryStream();
    stream.Write(png, 0, afterHeader);

    // Rendering intent 0, perceptual.
    WriteChunk(stream, "sRGB", [0]);
    if (dpi is { } dotsPerInch)
    {
        // Pixels per metre on both axes, then unit 1, the metre.
        var pixelsPerMetre = (uint) Math.Round(dotsPerInch / 0.0254);
        var physical = new byte[9];
        BinaryPrimitives.WriteUInt32BigEndian(physical, pixelsPerMetre);
        BinaryPrimitives.WriteUInt32BigEndian(physical.AsSpan(4), pixelsPerMetre);
        physical[8] = 1;
        WriteChunk(stream, "pHYs", physical);
    }

    stream.Write(png, afterHeader, png.Length - afterHeader);
    return stream.ToArray();
}

static void WriteChunk(Stream stream, string type, byte[] data)
{
    var typeAndData = new byte[4 + data.Length];
    System.Text.Encoding.ASCII.GetBytes(type, typeAndData);
    data.CopyTo(typeAndData, 4);

    Span<byte> number = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(number, (uint) data.Length);
    stream.Write(number);
    stream.Write(typeAndData);
    BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typeAndData));
    stream.Write(number);
}

// The CRC-32 of PNG chunks (ISO 3309), bit by bit: the files are small.
static uint Crc32(byte[] bytes)
{
    var crc = 0xFFFFFFFFu;
    foreach (var value in bytes)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
    }

    return ~crc;
}
