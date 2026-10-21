using System.Numerics;

namespace EasyClothes.Core.Textures.Editing;

public static class TextureCompositor
{
    private static readonly Lock CacheLock = new();
    private static (RgbaImage Source, RgbaImage Scaled)? _scaledBase;

    // maxSide limits the output for live previews; the saved texture is composed at full size.
    public static RgbaImage Compose(TextureEdit edit, int maxSide = int.MaxValue)
    {
        var source = edit.Base;
        var scale = Math.Min(1f, (float)maxSide / Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)(source.Width * scale));
        var height = Math.Max(1, (int)(source.Height * scale));
        var output = new RgbaImage(width, height, (byte[])ScaledBase(source, width, height).Pixels.Clone());

        if (edit.HasAdjustments) Adjust(output, edit);
        foreach (var decal in edit.Decals) DrawDecal(output, decal);
        if (edit.HasPaint) Overlay(output, edit.Paint);
        return output;
    }

    // Live previews compose many times per second over the same base; downscaling it once is the expensive part.
    private static RgbaImage ScaledBase(RgbaImage source, int width, int height)
    {
        if (source.Width == width && source.Height == height) return source;
        lock (CacheLock)
        {
            if (_scaledBase is { } cached && ReferenceEquals(cached.Source, source) && cached.Scaled.Width == width && cached.Scaled.Height == height)
                return cached.Scaled;
        }
        var scaled = Resample(source, width, height);
        lock (CacheLock) _scaledBase = (source, scaled);
        return scaled;
    }

    private static void Adjust(RgbaImage image, TextureEdit edit)
    {
        var colour = ColourMatrix(edit.Hue, edit.Saturation);
        var contrast = 1 + edit.Contrast;
        var tint = new Vector3((edit.TintRgb >> 16) & 0xFF, (edit.TintRgb >> 8) & 0xFF, edit.TintRgb & 0xFF) / 255f;
        var luma = new Vector3(0.299f, 0.587f, 0.114f);
        var pixels = image.Pixels;

        Parallel.For(0, image.Height, y =>
        {
            for (var o = y * image.Width * 4; o < (y + 1) * image.Width * 4; o += 4)
            {
                var c = new Vector3(pixels[o], pixels[o + 1], pixels[o + 2]) / 255f;
                c = Vector3.Transform(c, colour);
                c = (c - new Vector3(0.5f)) * contrast + new Vector3(0.5f + edit.Brightness);
                if (edit.TintStrength > 0)
                    c = Vector3.Lerp(c, tint * Vector3.Dot(c, luma) * 1.6f, edit.TintStrength);
                c = Vector3.Clamp(c, Vector3.Zero, Vector3.One) * 255f;
                pixels[o] = (byte)(c.X + 0.5f);
                pixels[o + 1] = (byte)(c.Y + 0.5f);
                pixels[o + 2] = (byte)(c.Z + 0.5f);
            }
        });
    }

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

    private static void DrawDecal(RgbaImage target, Decal decal)
    {
        var width = decal.Scale * target.Width;
        var height = width * decal.Image.Height / decal.Image.Width;
        if (width < 1 || height < 1) return;
        var centre = new Vector2(decal.X * target.Width, decal.Y * target.Height);
        var angle = decal.Rotation * MathF.PI / 180;
        var (cos, sin) = (MathF.Cos(angle), MathF.Sin(angle));
        var reach = MathF.Sqrt(width * width + height * height) / 2;

        var minX = Math.Max(0, (int)(centre.X - reach));
        var maxX = Math.Min(target.Width - 1, (int)(centre.X + reach));
        var minY = Math.Max(0, (int)(centre.Y - reach));
        var maxY = Math.Min(target.Height - 1, (int)(centre.Y + reach));
        var pixels = target.Pixels;

        Parallel.For(minY, maxY + 1, y =>
        {
            for (var x = minX; x <= maxX; x++)
            {
                var d = new Vector2(x + 0.5f, y + 0.5f) - centre;
                var local = new Vector2(d.X * cos + d.Y * sin, -d.X * sin + d.Y * cos);
                var u = local.X / width + 0.5f;
                var v = local.Y / height + 0.5f;
                if (u is < 0 or >= 1 || v is < 0 or >= 1) continue;
                var texel = Bilinear(decal.Image, u, v);
                Blend(pixels, (y * target.Width + x) * 4, texel, texel.W * decal.Opacity);
            }
        });
    }

    // Nearest sampling: at full size it is exact, and for previews it avoids resampling the layer per frame.
    private static void Overlay(RgbaImage target, RgbaImage paint)
    {
        var pixels = target.Pixels;
        Parallel.For(0, target.Height, y =>
        {
            var py = y * paint.Height / target.Height;
            for (var x = 0; x < target.Width; x++)
            {
                var p = (py * paint.Width + x * paint.Width / target.Width) * 4;
                var alpha = paint.Pixels[p + 3];
                if (alpha == 0) continue;
                Blend(pixels, (y * target.Width + x) * 4, new Vector4(paint.Pixels[p], paint.Pixels[p + 1], paint.Pixels[p + 2], alpha) / 255f, alpha / 255f);
            }
        });
    }

    // The texture's own alpha is kept: it is the garment's cut-out, decals and paint only colour it.
    private static void Blend(byte[] pixels, int o, Vector4 colour, float alpha)
    {
        if (alpha <= 0) return;
        pixels[o] = (byte)(pixels[o] + (colour.X * 255 - pixels[o]) * alpha + 0.5f);
        pixels[o + 1] = (byte)(pixels[o + 1] + (colour.Y * 255 - pixels[o + 1]) * alpha + 0.5f);
        pixels[o + 2] = (byte)(pixels[o + 2] + (colour.Z * 255 - pixels[o + 2]) * alpha + 0.5f);
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
