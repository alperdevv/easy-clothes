namespace EasyClothes.Core.Textures;

public static class ImageResampler
{
    // Area-weighted box filter, applied separably; only meant for downscaling.
    public static RgbaImage Resize(RgbaImage source, int width, int height)
    {
        if (source.Width == width && source.Height == height) return source;
        var horizontal = ResizeAxis(source.Pixels, source.Width, source.Height, width, horizontalPass: true);
        var pixels = ResizeAxis(horizontal, width, source.Height, height, horizontalPass: false);
        return new RgbaImage(width, height, pixels);
    }

    private static byte[] ResizeAxis(byte[] src, int srcWidth, int srcHeight, int target, bool horizontalPass)
    {
        var srcLength = horizontalPass ? srcWidth : srcHeight;
        var lines = horizontalPass ? srcHeight : srcWidth;
        var outWidth = horizontalPass ? target : srcWidth;
        var output = new byte[(horizontalPass ? target * srcHeight : srcWidth * target) * 4];
        var scale = (double)srcLength / target;
        Span<double> sum = stackalloc double[4];

        for (var line = 0; line < lines; line++)
        for (var i = 0; i < target; i++)
        {
            double start = i * scale, end = start + scale;
            sum.Clear();
            for (var s = (int)start; s < Math.Min(srcLength, (int)Math.Ceiling(end)); s++)
            {
                var weight = Math.Min(end, s + 1) - Math.Max(start, s);
                var offset = (horizontalPass ? line * srcWidth + s : s * srcWidth + line) * 4;
                for (var c = 0; c < 4; c++) sum[c] += src[offset + c] * weight;
            }
            var outOffset = (horizontalPass ? line * outWidth + i : i * outWidth + line) * 4;
            for (var c = 0; c < 4; c++) output[outOffset + c] = (byte)Math.Clamp(Math.Round(sum[c] / scale), 0, 255);
        }
        return output;
    }
}
