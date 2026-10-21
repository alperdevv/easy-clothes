using System.Numerics;

namespace EasyClothes.Core.Textures.Editing;

public sealed record BrushSettings(uint Rgb, float Radius, float Hardness, float Opacity, bool Erase);

public static class Brush
{
    // Stamps dabs every quarter radius along the segment so fast strokes stay continuous.
    public static void Stroke(RgbaImage layer, Vector2 from, Vector2 to, BrushSettings brush)
    {
        var spacing = Math.Max(0.5f, brush.Radius / 4);
        var steps = Math.Max(1, (int)(Vector2.Distance(from, to) / spacing));
        for (var i = 0; i <= steps; i++)
            Dab(layer, Vector2.Lerp(from, to, (float)i / steps), brush);
    }

    private static void Dab(RgbaImage layer, Vector2 centre, BrushSettings brush)
    {
        var r = brush.Radius;
        var (red, green, blue) = ((byte)(brush.Rgb >> 16), (byte)(brush.Rgb >> 8), (byte)brush.Rgb);
        var minX = Math.Max(0, (int)(centre.X - r));
        var maxX = Math.Min(layer.Width - 1, (int)(centre.X + r));
        var minY = Math.Max(0, (int)(centre.Y - r));
        var maxY = Math.Min(layer.Height - 1, (int)(centre.Y + r));
        var hard = Math.Clamp(brush.Hardness, 0, 0.999f);

        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre) / r;
            if (distance >= 1) continue;
            var falloff = distance <= hard ? 1 : 1 - (distance - hard) / (1 - hard);
            var strength = falloff * brush.Opacity;
            var o = (y * layer.Width + x) * 4;
            var alpha = layer.Pixels[o + 3] / 255f;
            if (brush.Erase)
            {
                layer.Pixels[o + 3] = (byte)(alpha * (1 - strength) * 255 + 0.5f);
                continue;
            }
            var newAlpha = alpha + (1 - alpha) * strength;
            var mix = strength / Math.Max(newAlpha, 1e-6f);
            layer.Pixels[o] = (byte)(layer.Pixels[o] + (red - layer.Pixels[o]) * mix + 0.5f);
            layer.Pixels[o + 1] = (byte)(layer.Pixels[o + 1] + (green - layer.Pixels[o + 1]) * mix + 0.5f);
            layer.Pixels[o + 2] = (byte)(layer.Pixels[o + 2] + (blue - layer.Pixels[o + 2]) * mix + 0.5f);
            layer.Pixels[o + 3] = (byte)(newAlpha * 255 + 0.5f);
        }
    }
}
