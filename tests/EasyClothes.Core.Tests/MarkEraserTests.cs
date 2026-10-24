using EasyClothes.Core.Textures;
using EasyClothes.Core.Textures.Editing;

namespace EasyClothes.Core.Tests;

public class MarkEraserTests
{
    [Fact]
    public void A_logo_on_striped_fabric_is_covered_with_matching_stripes()
    {
        var fabric = Fabric(256, 256);
        var marked = fabric with { Pixels = (byte[])fabric.Pixels.Clone() };
        var logo = new PixelRect(100, 90, 40, 30);
        for (var y = logo.Y; y < logo.Bottom; y++)
        for (var x = logo.X; x < logo.Right; x++)
            Set(marked, x, y, 230, 20, 20);

        var sources = MarkEraser.FindSources(marked, logo);
        var layer = MarkEraser.Fill(marked, logo, sources[0]);

        Assert.True(sources.Count > 1);
        double error = 0;
        for (var y = logo.Y; y < logo.Bottom; y++)
        for (var x = logo.X; x < logo.Right; x++)
        {
            var o = (y * 256 + x) * 4;
            Assert.Equal(255, layer.Pixels[o + 3]);
            for (var c = 0; c < 3; c++) error += Math.Abs(layer.Pixels[o + c] - fabric.Pixels[o + c]);
        }
        Assert.InRange(error / (logo.Width * logo.Height * 3), 0, 12);
        Assert.Equal(0, layer.Pixels[3]);
    }

    [Fact]
    public void Shading_around_the_mark_carries_into_the_patch()
    {
        // Flat cloth that gets darker to the right: a copy from the bright left must be darkened to fit.
        var cloth = new RgbaImage(128, 64, new byte[128 * 64 * 4]);
        for (var y = 0; y < 64; y++)
        for (var x = 0; x < 128; x++)
            Set(cloth, x, y, (byte)(200 - x), (byte)(200 - x), (byte)(200 - x));
        var mark = new PixelRect(96, 24, 12, 12);

        var layer = MarkEraser.Fill(cloth, mark, new PatchSource(-80, 0, 0));

        var o = (30 * 128 + 102) * 4;
        Assert.InRange(layer.Pixels[o], 200 - 102 - 6, 200 - 102 + 6);
    }

    // Vertical stripes 12 px apart with a little noise, like a knit.
    private static RgbaImage Fabric(int width, int height)
    {
        var random = new Random(7);
        var image = new RgbaImage(width, height, new byte[width * height * 4]);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var stripe = x % 12 < 4 ? 60 : 160;
            var noise = random.Next(-6, 7);
            Set(image, x, y, (byte)(stripe + noise), (byte)(stripe / 2 + noise), (byte)(90 + noise));
        }
        return image;
    }

    private static void Set(RgbaImage image, int x, int y, byte r, byte g, byte b)
    {
        var o = (y * image.Width + x) * 4;
        (image.Pixels[o], image.Pixels[o + 1], image.Pixels[o + 2], image.Pixels[o + 3]) = (r, g, b, 255);
    }
}
