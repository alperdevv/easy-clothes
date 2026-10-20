namespace EasyClothes.Core.Textures;

public static class BlockDecoder
{
    public static int CompressedSize(TextureFormat format, int width, int height) => format switch
    {
        TextureFormat.Dxt1 => BlocksWide(width) * BlocksWide(height) * 8,
        TextureFormat.Dxt5 => BlocksWide(width) * BlocksWide(height) * 16,
        TextureFormat.A8R8G8B8 => width * height * 4,
        _ => throw new NotSupportedException($"Texture format {format} is not supported yet."),
    };

    public static RgbaImage Decode(TextureFormat format, int width, int height, ReadOnlySpan<byte> data)
    {
        var pixels = new byte[width * height * 4];
        switch (format)
        {
            case TextureFormat.Dxt1:
                DecodeBlocks(data, width, height, 8, pixels, (block, rgba) => DecodeColour(block, rgba, allowAlpha: true));
                break;
            case TextureFormat.Dxt5:
                DecodeBlocks(data, width, height, 16, pixels, (block, rgba) =>
                {
                    DecodeColour(block[8..], rgba, allowAlpha: false);
                    DecodeAlpha(block, rgba);
                });
                break;
            case TextureFormat.A8R8G8B8:
                for (var i = 0; i < width * height; i++)
                {
                    // Stored as B, G, R, A.
                    pixels[i * 4] = data[i * 4 + 2];
                    pixels[i * 4 + 1] = data[i * 4 + 1];
                    pixels[i * 4 + 2] = data[i * 4];
                    pixels[i * 4 + 3] = data[i * 4 + 3];
                }
                break;
            default:
                throw new NotSupportedException($"Texture format {format} is not supported yet.");
        }
        return new RgbaImage(width, height, pixels);
    }

    private delegate void BlockDecode(ReadOnlySpan<byte> block, Span<byte> rgba16);

    private static int BlocksWide(int pixels) => Math.Max(1, (pixels + 3) / 4);

    private static void DecodeBlocks(ReadOnlySpan<byte> data, int width, int height, int blockSize, byte[] pixels, BlockDecode decode)
    {
        Span<byte> block = stackalloc byte[16 * 4];
        var bw = BlocksWide(width);
        var bh = BlocksWide(height);
        for (var by = 0; by < bh; by++)
        for (var bx = 0; bx < bw; bx++)
        {
            decode(data.Slice((by * bw + bx) * blockSize, blockSize), block);
            for (var py = 0; py < 4; py++)
            for (var px = 0; px < 4; px++)
            {
                int x = bx * 4 + px, y = by * 4 + py;
                if (x >= width || y >= height) continue;
                block.Slice((py * 4 + px) * 4, 4).CopyTo(pixels.AsSpan((y * width + x) * 4, 4));
            }
        }
    }

    private static void DecodeColour(ReadOnlySpan<byte> block, Span<byte> rgba, bool allowAlpha)
    {
        var c0 = (ushort)(block[0] | block[1] << 8);
        var c1 = (ushort)(block[2] | block[3] << 8);
        Span<byte> palette = stackalloc byte[16];
        Expand565(c0, palette[..4]);
        Expand565(c1, palette[4..8]);
        if (c0 > c1 || !allowAlpha)
        {
            for (var c = 0; c < 3; c++)
            {
                palette[8 + c] = (byte)((2 * palette[c] + palette[4 + c]) / 3);
                palette[12 + c] = (byte)((palette[c] + 2 * palette[4 + c]) / 3);
            }
            palette[11] = palette[15] = 255;
        }
        else
        {
            for (var c = 0; c < 3; c++)
                palette[8 + c] = (byte)((palette[c] + palette[4 + c]) / 2);
            palette[11] = 255;
            palette[12] = palette[13] = palette[14] = palette[15] = 0; // transparent black
        }

        var bits = (uint)(block[4] | block[5] << 8 | block[6] << 16 | block[7] << 24);
        for (var i = 0; i < 16; i++)
            palette.Slice((int)((bits >> (2 * i)) & 3) * 4, 4).CopyTo(rgba.Slice(i * 4, 4));
    }

    private static void DecodeAlpha(ReadOnlySpan<byte> block, Span<byte> rgba)
    {
        Span<byte> alpha = stackalloc byte[8];
        alpha[0] = block[0];
        alpha[1] = block[1];
        if (alpha[0] > alpha[1])
        {
            for (var i = 1; i < 7; i++)
                alpha[i + 1] = (byte)(((7 - i) * alpha[0] + i * alpha[1]) / 7);
        }
        else
        {
            for (var i = 1; i < 5; i++)
                alpha[i + 1] = (byte)(((5 - i) * alpha[0] + i * alpha[1]) / 5);
            alpha[6] = 0;
            alpha[7] = 255;
        }

        ulong bits = 0;
        for (var i = 0; i < 6; i++)
            bits |= (ulong)block[2 + i] << (8 * i);
        for (var i = 0; i < 16; i++)
            rgba[i * 4 + 3] = alpha[(int)((bits >> (3 * i)) & 7)];
    }

    private static void Expand565(ushort c, Span<byte> rgba)
    {
        int r = (c >> 11) & 0x1F, g = (c >> 5) & 0x3F, b = c & 0x1F;
        rgba[0] = (byte)(r << 3 | r >> 2);
        rgba[1] = (byte)(g << 2 | g >> 4);
        rgba[2] = (byte)(b << 3 | b >> 2);
        rgba[3] = 255;
    }
}
