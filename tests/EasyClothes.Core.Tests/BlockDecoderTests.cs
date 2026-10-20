using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Tests;

public class BlockDecoderTests
{
    [Fact]
    public void Dxt1_block_interpolates_between_its_two_colours()
    {
        // c0 = pure red (0xF800), c1 = pure blue (0x001F); row 0 uses indices 0,1,2,3.
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
        // alpha0 = 255, alpha1 = 0; first pixel index 1 (alpha1), second index 2 (6/7 of alpha0).
        byte[] block = [255, 0, 0b010_001, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0];

        var image = BlockDecoder.Decode(TextureFormat.Dxt5, 4, 4, block);

        Assert.Equal(0, image.Pixels[3]);
        Assert.Equal(218, image.Pixels[7]);
        Assert.Equal(255, image.Pixels[0]);
    }

    [Fact]
    public void Images_smaller_than_a_block_are_cropped()
    {
        var image = BlockDecoder.Decode(TextureFormat.Dxt1, 2, 2, new byte[8]);
        Assert.Equal(2 * 2 * 4, image.Pixels.Length);
    }
}
