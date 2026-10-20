using EasyClothes.Core.Rage;

namespace EasyClothes.Core.Tests;

public class TextureDictionaryTests
{
    [Theory]
    [InlineData(0x31545844u, "DXT1")]
    [InlineData(0x35545844u, "DXT5")]
    [InlineData(21u, "A8R8G8B8")]
    [InlineData(0x7Fu, "0x7F")]
    public void Names_texture_formats(uint format, string expected) =>
        Assert.Equal(expected, TextureDictionary.FormatName(format));
}
