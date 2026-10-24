using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Tests;

public class TextureDictionaryTests
{
    [Theory]
    [InlineData(0x31545844u, "DXT1")]
    [InlineData(0x35545844u, "DXT5")]
    [InlineData(0x20374342u, "BC7")]
    [InlineData(0x32495441u, "ATI2")]
    [InlineData(21u, "A8R8G8B8")]
    [InlineData(32u, "A8B8G8R8")]
    [InlineData(0x7Fu, "0x7F")]
    public void Names_texture_formats(uint format, string expected) =>
        Assert.Equal(expected, TextureDictionary.FormatName(format));

    [Theory]
    [InlineData(0x20374342u, TextureFormat.Bc7)]
    [InlineData(0x31495441u, TextureFormat.Bc4)]
    [InlineData(0x32495441u, TextureFormat.Bc5)]
    [InlineData(0x33545844u, TextureFormat.Dxt3)]
    public void Recognises_four_cc_formats(uint format, TextureFormat expected) =>
        Assert.Equal(expected, new TextureInfo("t", 4, 4, TextureDictionary.FormatName(format), 1).PixelFormat);
}
