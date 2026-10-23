using SkiaSharp;

namespace EasyClothes.Core.Textures;

// Draws a line of text into an image with a transparent background, for printing names and numbers on clothes.
public static class TextImage
{
    public static RgbaImage Render(string text, uint rgb, int height = 256)
    {
        using var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, height * 0.8f);
        using var paint = new SKPaint { Color = new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb), IsAntialias = true };
        var width = Math.Max(1, (int)MathF.Ceiling(font.MeasureText(text)) + height / 8);
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            font.GetFontMetrics(out var metrics);
            var baseline = (height - (metrics.Descent - metrics.Ascent)) / 2 - metrics.Ascent;
            canvas.DrawText(text, height / 16f, baseline, SKTextAlign.Left, font, paint);
        }
        return new RgbaImage(width, height, bitmap.Bytes);
    }
}
