using System.Drawing;
using System.Drawing.Imaging;
using Pisum.Transcribe.Dictation;

namespace Pisum.Transcribe.Tests.Dictation;

[Trait(Traits.Category, Traits.Categories.Unit)]
public sealed class DictationIconsTests
{
    [Fact]
    public void Constructor_Always_CreatesFourDistinct32PixelIcons()
    {
        // Act
        var sut = new DictationIcons();

        // Assert
        Icon[] icons = [sut.Ready, sut.Recording, sut.Transcribing, sut.Unavailable];
        icons.ShouldAllBe(icon => icon.Width == 32 && icon.Height == 32);
        icons.Select(Pixels).Distinct().Count().ShouldBe(4);
    }

    private static string Pixels(Icon icon)
    {
        using var bitmap = icon.ToBitmap();
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return Convert.ToBase64String(bytes);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
