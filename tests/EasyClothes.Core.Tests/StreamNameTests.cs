using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;

namespace EasyClothes.Core.Tests;

public class StreamNameTests
{
    [Fact]
    public void Parses_a_component_model_with_ped_prefix()
    {
        var model = StreamName.ParseModel("mp_f_freemode_01_mp_f_eup^jbib_040_u.ydd");

        Assert.Equal(new ModelName(PedGender.Female, new SlotRef(ComponentSlot.Top, null), 40), model);
    }

    [Fact]
    public void Parses_a_prop_model_and_texture()
    {
        Assert.Equal(new ModelName(PedGender.Male, new SlotRef(null, PropSlot.Hat), 12),
            StreamName.ParseModel("mp_m_freemode_01_p_mp_m_eup^p_head_012.ydd"));
        Assert.Equal(new TextureName(PedGender.Male, new SlotRef(null, PropSlot.Hat), 12, 2),
            StreamName.ParseTexture("mp_m_freemode_01_p_mp_m_eup^p_head_diff_012_c.ytd"));
    }

    [Fact]
    public void Parses_bare_names_without_gender()
    {
        Assert.Equal(new TextureName(null, new SlotRef(ComponentSlot.Legs, null), 4, 0),
            StreamName.ParseTexture("LOWR_DIFF_004_A_UNI.ytd"));
    }

    [Theory]
    [InlineData("jacket.ydd")]
    [InlineData("xxxx_000_u.ydd")]
    [InlineData("jbib_diff_000_a_uni.ytd")]
    public void Rejects_names_that_are_not_models(string name) =>
        Assert.Null(StreamName.ParseModel(name));
}
