using BCnEncoder.Encoder;
using BCnEncoder.Shared;

namespace EasyClothes.Core.Textures;

public sealed record EncodedTexture(int Width, int Height, TextureFormat Format, IReadOnlyList<byte[]> Mips);

public static class TextureEncoder
{
    // Mips stop at 4×4 so every level is at least one compressed block.
    public static EncodedTexture Encode(RgbaImage image, TextureFormat format)
    {
        var compression = format switch
        {
            TextureFormat.Dxt1 => HasTransparency(image) ? CompressionFormat.Bc1WithAlpha : CompressionFormat.Bc1,
            TextureFormat.Dxt5 => CompressionFormat.Bc3,
            _ => throw new NotSupportedException($"Cannot encode to {format}."),
        };
        var encoder = new BcEncoder
        {
            OutputOptions =
            {
                Format = compression,
                Quality = CompressionQuality.Balanced,
                GenerateMipMaps = true,
                MaxMipMapLevel = MipCount(image.Width, image.Height),
            },
        };
        var mips = encoder.EncodeToRawBytes(image.Pixels, image.Width, image.Height, PixelFormat.Rgba32);
        return new EncodedTexture(image.Width, image.Height, format, mips);
    }

    public static int MipCount(int width, int height) =>
        Math.Max(1, (int)Math.Log2(Math.Min(width, height)) - 1);

    public static bool HasTransparency(RgbaImage image)
    {
        for (var i = 3; i < image.Pixels.Length; i += 4)
            if (image.Pixels[i] < 255) return true;
        return false;
    }
}
