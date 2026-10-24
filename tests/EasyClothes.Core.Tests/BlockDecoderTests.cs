using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Tests;

public class BlockDecoderTests
{
    [Fact]
    public void Dxt1_block_interpolates_between_its_two_colours()
    {
        // c0 = red, c1 = blue; the first row uses indices 0, 1, 2, 3.
        byte[] block = [0x00, 0xF8, 0x1F, 0x00, 0b11_10_01_00, 0, 0, 0];

        var image = BlockDecoder.Decode(TextureFormat.Dxt1, 4, 4, block);

        Assert.Equal([255, 0, 0, 255], image.Pixels[0..4]);
        Assert.Equal([0, 0, 255, 255], image.Pixels[4..8]);
        Assert.Equal([170, 0, 85, 255], image.Pixels[8..12]);
        Assert.Equal([85, 0, 170, 255], image.Pixels[12..16]);
    }

    [Fact]
    public void Dxt1_with_c0_not_greater_than_c1_has_a_transparent_index()
    {
        byte[] block = [0x1F, 0x00, 0x00, 0xF8, 0b11, 0, 0, 0];

        var image = BlockDecoder.Decode(TextureFormat.Dxt1, 4, 4, block);

        Assert.Equal([0, 0, 0, 0], image.Pixels[0..4]);
    }

    [Fact]
    public void Dxt5_alpha_uses_the_eight_step_ramp()
    {
        // alpha0 = 255, alpha1 = 0; pixel 0 uses index 1, pixel 1 index 2 (6/7 of alpha0).
        byte[] block = [255, 0, 0b010_001, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0];

        var image = BlockDecoder.Decode(TextureFormat.Dxt5, 4, 4, block);

        Assert.Equal(0, image.Pixels[3]);
        Assert.Equal(218, image.Pixels[7]);
        Assert.Equal(255, image.Pixels[0]);
    }

    [Fact]
    public void A8b8g8r8_is_already_in_rgba_order()
    {
        var image = BlockDecoder.Decode(TextureFormat.A8B8G8R8, 1, 1, [10, 20, 30, 40]);
        Assert.Equal([10, 20, 30, 40], image.Pixels);
    }

    [Fact]
    public void Images_smaller_than_a_block_are_cropped()
    {
        var image = BlockDecoder.Decode(TextureFormat.Dxt1, 2, 2, new byte[8]);
        Assert.Equal(2 * 2 * 4, image.Pixels.Length);
    }

    [Theory]
    [InlineData(TextureFormat.Bc7, CompressionFormat.Bc7, 8)]
    [InlineData(TextureFormat.Dxt3, CompressionFormat.Bc2, 40)]
    public void Bcn_colour_formats_decode_close_to_the_original(TextureFormat format, CompressionFormat compression, int tolerance)
    {
        var original = Gradient(16, 8);
        var data = new BcEncoder(compression).EncodeToRawBytes(original, 16, 8, PixelFormat.Rgba32, 0, out _, out _);

        var image = BlockDecoder.Decode(format, 16, 8, data);

        Assert.Equal(BlockDecoder.CompressedSize(format, 16, 8), data.Length);
        for (var i = 0; i < original.Length; i++)
            Assert.InRange(image.Pixels[i], original[i] - tolerance, original[i] + tolerance);
    }

    [Fact]
    public void Bc4_decodes_to_grey()
    {
        var original = Gradient(8, 8);
        var data = new BcEncoder(CompressionFormat.Bc4).EncodeToRawBytes(original, 8, 8, PixelFormat.Rgba32, 0, out _, out _);

        var image = BlockDecoder.Decode(TextureFormat.Bc4, 8, 8, data);

        Assert.Equal(BlockDecoder.CompressedSize(TextureFormat.Bc4, 8, 8), data.Length);
        for (var i = 0; i < 8 * 8 * 4; i += 4)
        {
            Assert.InRange(image.Pixels[i], original[i] - 8, original[i] + 8);
            Assert.Equal(image.Pixels[i], image.Pixels[i + 1]);
            Assert.Equal(image.Pixels[i], image.Pixels[i + 2]);
        }
    }

    [Fact]
    public void Bc5_keeps_red_and_green()
    {
        var original = Gradient(8, 8);
        var data = new BcEncoder(CompressionFormat.Bc5).EncodeToRawBytes(original, 8, 8, PixelFormat.Rgba32, 0, out _, out _);

        var image = BlockDecoder.Decode(TextureFormat.Bc5, 8, 8, data);

        for (var i = 0; i < 8 * 8 * 4; i += 4)
        {
            Assert.InRange(image.Pixels[i], original[i] - 8, original[i] + 8);
            Assert.InRange(image.Pixels[i + 1], original[i + 1] - 8, original[i + 1] + 8);
        }
    }

    private static byte[] Gradient(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            pixels[i] = (byte)(x * 255 / (width - 1));
            pixels[i + 1] = (byte)(y * 255 / (height - 1));
            pixels[i + 2] = 128;
            pixels[i + 3] = (byte)(x < width / 2 ? 255 : 160);
        }
        return pixels;
    }
}
