using EasyClothes.Core.Model;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Rage.Meta;

namespace EasyClothes.Core.Tests;

public class PedVariationTests
{
    [Fact]
    public void Written_ymt_reads_back_with_the_same_drawables_and_props()
    {
        var components = new Dictionary<ComponentSlot, IReadOnlyList<int>>
        {
            [ComponentSlot.Top] = [3, 1, 26],
            [ComponentSlot.Mask] = [2],
        };
        var props = new Dictionary<PropSlot, IReadOnlyList<int>>
        {
            [PropSlot.Hat] = [1, 4],
            [PropSlot.Watch] = [2],
        };

        var file = Roundtrip(PedVariation.Write("mp_m_test", components, props));
        var read = PedVariation.Read(file);

        Assert.Equal(Jenkins.Hash("mp_m_test"), read.DlcNameHash);
        Assert.Equal([ComponentSlot.Mask, ComponentSlot.Top], read.Components.Keys.Order());
        Assert.Equal([3, 1, 26], read.Components[ComponentSlot.Top]);
        Assert.Equal([2], read.Components[ComponentSlot.Mask]);
        Assert.Equal([1, 4], read.Props[PropSlot.Hat]);
        Assert.Equal([2], read.Props[PropSlot.Watch]);
        Assert.Equal(0u, file.Header.GraphicsFlags & 0x0FFFFFFF);
    }

    [Fact]
    public void Ymt_carries_the_full_reflection_schema()
    {
        var file = PedVariation.Write("mp_f_test", new Dictionary<ComponentSlot, IReadOnlyList<int>> { [ComponentSlot.Legs] = [1] }, new Dictionary<PropSlot, IReadOnlyList<int>>());
        var meta = new MetaReader(file);

        Assert.Equal(PedVariationSchema.PedVariationInfo, meta.Blocks[meta.RootIndex].Type);
        Assert.Equal(0x70, meta.Root.Length);
        Assert.Equal(PedVariationSchema.Structs.Length, BitConverter.ToUInt16(file.SystemData, 0x48));
        Assert.Equal(PedVariationSchema.Enums.Length, BitConverter.ToUInt16(file.SystemData, 0x4A));
    }

    [Fact]
    public void Rejects_slots_the_game_cannot_index()
    {
        var tooMany = new Dictionary<ComponentSlot, IReadOnlyList<int>> { [ComponentSlot.Top] = Enumerable.Repeat(1, 256).ToArray() };
        var noTextures = new Dictionary<ComponentSlot, IReadOnlyList<int>> { [ComponentSlot.Top] = [0] };
        var none = new Dictionary<PropSlot, IReadOnlyList<int>>();

        Assert.Throws<InvalidDataException>(() => PedVariation.Write("x", tooMany, none));
        Assert.Throws<InvalidDataException>(() => PedVariation.Write("x", noTextures, none));
    }

    [Fact]
    public void Jenkins_hash_matches_known_game_names()
    {
        Assert.Equal(0x7E623533u, Jenkins.Hash("mp_m_eup"));
        Assert.Equal(PedVariationSchema.PedVariationInfo, Jenkins.Hash("CPedVariationInfo", lowercase: false));
    }

    private static ResourceFile Roundtrip(ResourceFile file)
    {
        using var stream = new MemoryStream();
        file.Write(stream);
        stream.Position = 0;
        return ResourceFile.Read(stream);
    }
}
