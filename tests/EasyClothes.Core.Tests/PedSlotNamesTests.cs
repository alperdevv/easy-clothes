using EasyClothes.Core.Model;

namespace EasyClothes.Core.Tests;

public class PedSlotNamesTests
{
    [Fact]
    public void Every_component_and_prop_has_a_file_code()
    {
        foreach (var slot in Enum.GetValues<ComponentSlot>())
            Assert.Matches("^[a-z]{4}$", PedSlotNames.FileCode(slot));
        foreach (var slot in Enum.GetValues<PropSlot>())
            Assert.StartsWith("p_", PedSlotNames.FileCode(slot));
    }

    [Theory]
    [InlineData(0, 'a')]
    [InlineData(25, 'z')]
    public void Texture_letters_cover_a_to_z(int index, char letter) =>
        Assert.Equal(letter, TextureVariation.LetterFor(index));

    [Fact]
    public void Texture_index_beyond_z_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TextureVariation.LetterFor(Drawable.MaxTextures));
}
