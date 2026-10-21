using System.Numerics;
using System.Runtime.CompilerServices;

namespace EasyClothes.Core.Textures.Editing;

public static class TextureCompositor
{
    private static readonly Vector3 Luma = new(0.299f, 0.587f, 0.114f);

    // Layers are composited in straight alpha with the W3C blend-mode formulas, as image editors do.
    public static RgbaImage Compose(TextureDocument document)
    {
        var (width, height) = (document.Width, document.Height);
        var output = new float[width * height * 4];
        foreach (var layer in document.Layers)
        {
            if (!layer.Visible || layer.Opacity <= 0) continue;
            switch (layer)
            {
                case PixelLayer pixels:
                    DrawPixels(output, width, height, Resample(pixels.Image, width, height), layer);
                    break;
                case ImageLayer image:
                    DrawImage(output, width, height, image);
                    break;
                case FillLayer fill:
                    DrawFill(output, width, height, fill);
                    break;
                case AdjustmentLayer adjustment:
                    Adjust(output, width, height, adjustment);
                    break;
            }
        }

        var alpha = document.KeepOriginalAlpha && document.Original is { } original ? Resample(original, width, height).Pixels : null;
        var result = new byte[output.Length];
        Parallel.For(0, height, y =>
        {
            for (var o = y * width * 4; o < (y + 1) * width * 4; o += 4)
            {
                result[o] = ToByte(output[o]);
                result[o + 1] = ToByte(output[o + 1]);
                result[o + 2] = ToByte(output[o + 2]);
                result[o + 3] = alpha?[o + 3] ?? ToByte(output[o + 3]);
            }
        });
        return new RgbaImage(width, height, result);
    }

    private static byte ToByte(float value) => (byte)(Math.Clamp(value, 0, 1) * 255 + 0.5f);

    private static void DrawPixels(float[] output, int width, int height, RgbaImage image, Layer layer)
    {
        var source = image.Pixels;
        Parallel.For(0, height, y =>
        {
            for (var o = y * width * 4; o < (y + 1) * width * 4; o += 4)
            {
                if (source[o + 3] == 0) continue;
                var colour = new Vector3(source[o], source[o + 1], source[o + 2]) / 255f;
                Composite(output, o, colour, source[o + 3] / 255f * layer.Opacity, layer.Blend);
            }
        });
    }

    private static void DrawFill(float[] output, int width, int height, FillLayer fill)
    {
        var colour = Unpack(fill.Rgb);
        Parallel.For(0, height, y =>
        {
            for (var o = y * width * 4; o < (y + 1) * width * 4; o += 4)
                Composite(output, o, colour, fill.Opacity, fill.Blend);
        });
    }

    private static void DrawImage(float[] output, int width, int height, ImageLayer layer)
    {
        var w = layer.Scale * width;
        var h = w * layer.Image.Height / layer.Image.Width;
        if (w < 1 || h < 1) return;
        var centre = new Vector2(layer.X * width, layer.Y * height);
        var angle = layer.Rotation * MathF.PI / 180;
        var (cos, sin) = (MathF.Cos(angle), MathF.Sin(angle));
        var reach = MathF.Sqrt(w * w + h * h) / 2;
        var minX = Math.Max(0, (int)(centre.X - reach));
        var maxX = Math.Min(width - 1, (int)(centre.X + reach));
        var minY = Math.Max(0, (int)(centre.Y - reach));
        var maxY = Math.Min(height - 1, (int)(centre.Y + reach));

        Parallel.For(minY, maxY + 1, y =>
        {
            for (var x = minX; x <= maxX; x++)
            {
                var d = new Vector2(x + 0.5f, y + 0.5f) - centre;
                var local = new Vector2(d.X * cos + d.Y * sin, -d.X * sin + d.Y * cos);
                var u = local.X / w + 0.5f;
                var v = local.Y / h + 0.5f;
                if (u is < 0 or >= 1 || v is < 0 or >= 1) continue;
                var texel = Bilinear(layer.Image, u, v);
                if (texel.W <= 0) continue;
                Composite(output, (y * width + x) * 4, new Vector3(texel.X, texel.Y, texel.Z), texel.W * layer.Opacity, layer.Blend);
            }
        });
    }

    private static void Adjust(float[] output, int width, int height, AdjustmentLayer layer)
    {
        var colour = ColourMatrix(layer.Hue, layer.Saturation);
        var contrast = 1 + layer.Contrast;
        var tint = Unpack(layer.TintRgb);
        Parallel.For(0, height, y =>
        {
            for (var o = y * width * 4; o < (y + 1) * width * 4; o += 4)
            {
                var c = new Vector3(output[o], output[o + 1], output[o + 2]);
                var adjusted = Vector3.Transform(c, colour);
                adjusted = (adjusted - new Vector3(0.5f)) * contrast + new Vector3(0.5f + layer.Brightness);
                if (layer.TintStrength > 0)
                    adjusted = Vector3.Lerp(adjusted, tint * Vector3.Dot(adjusted, Luma) * 1.6f, layer.TintStrength);
                c = Vector3.Lerp(c, Vector3.Clamp(adjusted, Vector3.Zero, Vector3.One), layer.Opacity);
                (output[o], output[o + 1], output[o + 2]) = (c.X, c.Y, c.Z);
            }
        });
    }

    // Source-over with the blended colour, per the W3C compositing spec: where the backdrop is
    // transparent the source shows unblended.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Composite(float[] output, int o, Vector3 source, float alpha, BlendMode mode)
    {
        if (alpha <= 0) return;
        var backdropAlpha = output[o + 3];
        var backdrop = new Vector3(output[o], output[o + 1], output[o + 2]);
        if (mode != BlendMode.Normal && backdropAlpha > 0)
            source = (1 - backdropAlpha) * source + backdropAlpha * Blend(backdrop, source, mode);
        var outAlpha = alpha + backdropAlpha * (1 - alpha);
        var colour = (source * alpha + backdrop * backdropAlpha * (1 - alpha)) / outAlpha;
        (output[o], output[o + 1], output[o + 2], output[o + 3]) = (colour.X, colour.Y, colour.Z, outAlpha);
    }

    private static Vector3 Blend(Vector3 b, Vector3 s, BlendMode mode) => mode switch
    {
        BlendMode.Multiply => b * s,
        BlendMode.Screen => b + s - b * s,
        BlendMode.Overlay => new Vector3(HardLight(s.X, b.X), HardLight(s.Y, b.Y), HardLight(s.Z, b.Z)),
        BlendMode.SoftLight => new Vector3(SoftLight(b.X, s.X), SoftLight(b.Y, s.Y), SoftLight(b.Z, s.Z)),
        BlendMode.Darken => Vector3.Min(b, s),
        BlendMode.Lighten => Vector3.Max(b, s),
        BlendMode.Add => Vector3.Min(b + s, Vector3.One),
        BlendMode.Difference => Vector3.Abs(b - s),
        BlendMode.Color => SetLuminosity(s, Vector3.Dot(b, Luma)),
        _ => s,
    };

    // Overlay is hard light with the layers swapped.
    private static float HardLight(float b, float s) => s <= 0.5f ? 2 * b * s : 1 - 2 * (1 - b) * (1 - s);

    private static float SoftLight(float b, float s)
    {
        if (s <= 0.5f) return b - (1 - 2 * s) * b * (1 - b);
        var d = b <= 0.25f ? ((16 * b - 12) * b + 4) * b : MathF.Sqrt(b);
        return b + (2 * s - 1) * (d - b);
    }

    private static Vector3 SetLuminosity(Vector3 c, float luminosity)
    {
        c += new Vector3(luminosity - Vector3.Dot(c, Luma));
        var l = Vector3.Dot(c, Luma);
        var min = MathF.Min(c.X, MathF.Min(c.Y, c.Z));
        var max = MathF.Max(c.X, MathF.Max(c.Y, c.Z));
        if (min < 0) c = new Vector3(l) + (c - new Vector3(l)) * l / (l - min);
        if (max > 1) c = new Vector3(l) + (c - new Vector3(l)) * (1 - l) / (max - l);
        return c;
    }

    private static Vector3 Unpack(uint rgb) => new Vector3((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF) / 255f;

    // Hue rotation about the grey axis followed by a saturation scale, both linear in RGB.
    private static Matrix4x4 ColourMatrix(float hueDegrees, float saturation)
    {
        var a = hueDegrees * MathF.PI / 180;
        var (cos, sin) = (MathF.Cos(a), MathF.Sin(a));
        var k = 1 / 3f;
        var sq = MathF.Sqrt(k);
        var hue = new Matrix4x4(
            cos + (1 - cos) * k, k * (1 - cos) + sq * sin, k * (1 - cos) - sq * sin, 0,
            k * (1 - cos) - sq * sin, cos + k * (1 - cos), k * (1 - cos) + sq * sin, 0,
            k * (1 - cos) + sq * sin, k * (1 - cos) - sq * sin, cos + k * (1 - cos), 0,
            0, 0, 0, 1);

        var s = 1 + saturation;
        var (r, g, b) = (0.299f * (1 - s), 0.587f * (1 - s), 0.114f * (1 - s));
        var sat = new Matrix4x4(
            r + s, r, r, 0,
            g, g + s, g, 0,
            b, b, b + s, 0,
            0, 0, 0, 1);
        return hue * sat;
    }

    public static RgbaImage Resample(RgbaImage source, int width, int height)
    {
        if (source.Width == width && source.Height == height) return source;
        if (width <= source.Width && height <= source.Height) return ImageResampler.Resize(source, width, height);
        var pixels = new byte[width * height * 4];
        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var c = Bilinear(source, (x + 0.5f) / width, (y + 0.5f) / height) * 255f;
                var o = (y * width + x) * 4;
                pixels[o] = (byte)(c.X + 0.5f);
                pixels[o + 1] = (byte)(c.Y + 0.5f);
                pixels[o + 2] = (byte)(c.Z + 0.5f);
                pixels[o + 3] = (byte)(c.W + 0.5f);
            }
        });
        return new RgbaImage(width, height, pixels);
    }

    private static Vector4 Bilinear(RgbaImage image, float u, float v)
    {
        var fx = Math.Clamp(u * image.Width - 0.5f, 0, image.Width - 1);
        var fy = Math.Clamp(v * image.Height - 0.5f, 0, image.Height - 1);
        var (x0, y0) = ((int)fx, (int)fy);
        var (x1, y1) = (Math.Min(x0 + 1, image.Width - 1), Math.Min(y0 + 1, image.Height - 1));
        var (tx, ty) = (fx - x0, fy - y0);
        var top = Vector4.Lerp(Texel(image, x0, y0), Texel(image, x1, y0), tx);
        var bottom = Vector4.Lerp(Texel(image, x0, y1), Texel(image, x1, y1), tx);
        return Vector4.Lerp(top, bottom, ty) / 255f;
    }

    private static Vector4 Texel(RgbaImage image, int x, int y)
    {
        var o = (y * image.Width + x) * 4;
        return new Vector4(image.Pixels[o], image.Pixels[o + 1], image.Pixels[o + 2], image.Pixels[o + 3]);
    }
}
