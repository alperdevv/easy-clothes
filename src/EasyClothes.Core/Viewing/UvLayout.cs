using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Viewing;

// The model's UV islands drawn over its texture, so a person (or Claude) can tell which part of the
// image lands where on the garment. Quarter lines help reading coordinates off the picture.
public static class UvLayout
{
    private static readonly byte[] Edge = [0x5A, 0xE6, 0xFF, 0xFF];
    private static readonly byte[] Grid = [0xFF, 0x60, 0x60, 0xFF];

    public static RgbaImage Render(RgbaImage? texture, IEnumerable<MeshData> meshes, int maxSide)
    {
        var (width, height) = texture is null ? (maxSide, maxSide) : Fit(texture.Width, texture.Height, maxSide);
        var image = texture is null
            ? new RgbaImage(width, height, Enumerable.Repeat((byte)0x30, width * height * 4).ToArray())
            : texture.Width == width && texture.Height == height ? texture with { Pixels = (byte[])texture.Pixels.Clone() } : ImageResampler.Resize(texture, width, height);
        Darken(image);

        for (var q = 1; q < 4; q++)
        {
            Line(image, q * width / 4f, 0, q * width / 4f, height - 1, Grid);
            Line(image, 0, q * height / 4f, width - 1, q * height / 4f, Grid);
        }
        foreach (var mesh in meshes)
        {
            for (var t = 0; t + 2 < mesh.Indices.Length; t += 3)
            {
                for (var e = 0; e < 3; e++)
                {
                    int a = mesh.Indices[t + e], b = mesh.Indices[t + (e + 1) % 3];
                    Line(image, Wrap(mesh.TexCoords[a * 2]) * width, Wrap(mesh.TexCoords[a * 2 + 1]) * height,
                        Wrap(mesh.TexCoords[b * 2]) * width, Wrap(mesh.TexCoords[b * 2 + 1]) * height, Edge);
                }
            }
        }
        return image;
    }

    private static (int, int) Fit(int width, int height, int maxSide)
    {
        var scale = Math.Min(1.0, (double)maxSide / Math.Max(width, height));
        return (Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
    }

    private static float Wrap(float value) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;

    // Dimmed so the lines stand out on any texture.
    private static void Darken(RgbaImage image)
    {
        for (var i = 0; i < image.Pixels.Length; i += 4)
        {
            for (var c = 0; c < 3; c++) image.Pixels[i + c] = (byte)(image.Pixels[i + c] * 0.6f);
            image.Pixels[i + 3] = 255;
        }
    }

    private static void Line(RgbaImage image, float x0, float y0, float x1, float y1, byte[] colour)
    {
        var steps = (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)));
        for (var i = 0; i <= steps; i++)
        {
            var f = steps == 0 ? 0 : (float)i / steps;
            var x = (int)(x0 + (x1 - x0) * f);
            var y = (int)(y0 + (y1 - y0) * f);
            if (x < 0 || y < 0 || x >= image.Width || y >= image.Height) continue;
            colour.CopyTo(image.Pixels, (y * image.Width + x) * 4);
        }
    }
}
