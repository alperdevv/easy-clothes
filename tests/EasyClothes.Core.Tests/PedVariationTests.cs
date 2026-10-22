using EasyClothes.Core.Model;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Rage.Meta;

namespace EasyClothes.Core.Tests;

public class PedVariationTests
{
    [Fact]
    public void Written_ymt_reads_back_with_the_same_drawables_and_props()
    {
        var components = new Dictionary<ComponentSlot, IReadOnlyList<VariationDrawable>>
        {
            [ComponentSlot.Top] = [new(3) { FirstPerson = true }, new(1) { Cloth = true }, new(26)],
            [ComponentSlot.Mask] = [new(2)],
            [ComponentSlot.Shoes] = [new(1), new(2) { HeelHeight = 1.4f }],
        };
        var props = new Dictionary<PropSlot, IReadOnlyList<VariationDrawable>>
        {
            [PropSlot.Hat] = [new(1) { HairScale = -0.5f }, new(4)],
            [PropSlot.Watch] = [new(2)],
        };

        var file = Roundtrip(PedVariation.Write("mp_m_test", components, props));
        var read = PedVariation.Read(file);

        Assert.Equal(Jenkins.Hash("mp_m_test"), read.DlcNameHash);
        Assert.Equal([ComponentSlot.Mask, ComponentSlot.Shoes, ComponentSlot.Top], read.Components.Keys.Order());
        Assert.Equal(components[ComponentSlot.Top], read.Components[ComponentSlot.Top]);
        Assert.Equal(components[ComponentSlot.Mask], read.Components[ComponentSlot.Mask]);
        Assert.Equal(components[ComponentSlot.Shoes], read.Components[ComponentSlot.Shoes]);
        Assert.Equal(props[PropSlot.Hat], read.Props[PropSlot.Hat]);
        Assert.Equal(props[PropSlot.Watch], read.Props[PropSlot.Watch]);
        Assert.Equal(0u, file.Header.GraphicsFlags & 0x0FFFFFFF);
    }

    [Fact]
    public void Ymt_carries_the_full_reflection_schema()
    {
        var file = PedVariation.Write("mp_f_test", new Dictionary<ComponentSlot, IReadOnlyList<VariationDrawable>> { [ComponentSlot.Legs] = [new(1)] }, new Dictionary<PropSlot, IReadOnlyList<VariationDrawable>>());
        var meta = new MetaReader(file);

        Assert.Equal(PedVariationSchema.PedVariationInfo, meta.Blocks[meta.RootIndex].Type);
        Assert.Equal(0x70, meta.Root.Length);
        Assert.Equal(PedVariationSchema.Structs.Length, BitConverter.ToUInt16(file.SystemData, 0x48));
        Assert.Equal(PedVariationSchema.Enums.Length, BitConverter.ToUInt16(file.SystemData, 0x4A));
    }

    [Fact]
    public void Rejects_slots_the_game_cannot_index()
    {
        var tooMany = new Dictionary<ComponentSlot, IReadOnlyList<VariationDrawable>> { [ComponentSlot.Top] = Enumerable.Repeat(new VariationDrawable(1), 256).ToArray() };
        var noTextures = new Dictionary<ComponentSlot, IReadOnlyList<VariationDrawable>> { [ComponentSlot.Top] = [new(0)] };
        var none = new Dictionary<PropSlot, IReadOnlyList<VariationDrawable>>();

        Assert.Throws<InvalidDataException>(() => PedVariation.Write("x", tooMany, none));
        Assert.Throws<InvalidDataException>(() => PedVariation.Write("x", noTextures, none));
    }

    [Fact]
    public void Jenkins_hash_matches_known_game_names()
    {
        Assert.Equal(0x7E623533u, Jenkins.Hash("mp_m_eup"));
        Assert.Equal(PedVariationSchema.PedVariationInfo, Jenkins.Hash("CPedVariationInfo", lowercase: false));
        Assert.Equal(0x7735E17Fu, Jenkins.Hash("shoe_heels"));
    }

    private static ResourceFile Roundtrip(ResourceFile file)
    {
        using var stream = new MemoryStream();
        file.Write(stream);
        stream.Position = 0;
        return ResourceFile.Read(stream);
    }
}
