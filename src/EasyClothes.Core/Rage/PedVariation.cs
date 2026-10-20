using System.Buffers.Binary;
using EasyClothes.Core.Model;
using EasyClothes.Core.Rage.Meta;
using static EasyClothes.Core.Rage.Meta.PedVariationSchema;

namespace EasyClothes.Core.Rage;

// Contents of a ped variation .ymt: how many texture variations each drawable has,
// per component slot and per prop anchor, in drawable order.
public sealed record PedVariation(
    uint DlcNameHash,
    IReadOnlyDictionary<ComponentSlot, IReadOnlyList<int>> Components,
    IReadOnlyDictionary<PropSlot, IReadOnlyList<int>> Props)
{
    public const int MaxDrawablesPerSlot = 255;
    private const int ComponentSlots = 12;
    private const uint AudioNone = 0x1D632BA1;
    private const uint HatHairExpression = 0xBF000000; // -0.5f, as the game's own hats use

    public static ResourceFile Write(string dlcName,
        IReadOnlyDictionary<ComponentSlot, IReadOnlyList<int>> components,
        IReadOnlyDictionary<PropSlot, IReadOnlyList<int>> props)
    {
        var builder = new MetaBuilder();
        var root = new byte[0x70];
        var rootAt = builder.Append(PedVariationInfo, root);

        root[0] = root[1] = 1;
        root.AsSpan(4, ComponentSlots).Fill(0xFF);
        var usedSlots = components.Where(c => c.Value.Count > 0).OrderBy(c => c.Key).ToList();
        var componentData = new byte[usedSlots.Count * 0x18];
        for (var i = 0; i < usedSlots.Count; i++)
        {
            var (slot, textureCounts) = (usedSlots[i].Key, usedSlots[i].Value);
            CheckCount(slot.ToString(), textureCounts);
            root[4 + (int)slot] = (byte)i;

            var drawables = new byte[textureCounts.Count * 0x30];
            for (var d = 0; d < textureCounts.Count; d++)
            {
                var textures = new byte[textureCounts[d] * 3];
                for (var t = 0; t < textureCounts[d]; t++) textures[t * 3 + 1] = 0xFF;
                var item = drawables.AsSpan(d * 0x30, 0x30);
                item[0] = 1; // propMask: universal ("_u") drawable
                MetaBuilder.WriteArray(item[8..], builder.Append(TextureData, textures), textureCounts[d]);
            }
            var entry = componentData.AsSpan(i * 0x18, 0x18);
            entry[0] = (byte)Math.Min(255, textureCounts.Sum());
            MetaBuilder.WriteArray(entry[8..], builder.Append(DrawableData, drawables), textureCounts.Count);
        }
        MetaBuilder.WriteArray(root.AsSpan(0x10), usedSlots.Count > 0 ? builder.Append(ComponentData, componentData) : null, usedSlots.Count);

        var infos = usedSlots.SelectMany(s => s.Value.Select((_, d) => (Slot: s.Key, Drawable: d))).ToList();
        var componentInfos = new byte[infos.Count * 0x30];
        for (var i = 0; i < infos.Count; i++)
        {
            var item = componentInfos.AsSpan(i * 0x30, 0x30);
            BinaryPrimitives.WriteUInt32LittleEndian(item, AudioNone);
            item[0x2C] = (byte)infos[i].Slot;
            item[0x2D] = (byte)infos[i].Drawable;
        }
        MetaBuilder.WriteArray(root.AsSpan(0x30), infos.Count > 0 ? builder.Append(ComponentInfo, componentInfos) : null, infos.Count);

        WriteProps(builder, root.AsSpan(0x40, 0x28), props);
        BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(0x68), Jenkins.Hash(dlcName));
        builder.Patch(rootAt, root);

        return builder.Build(dlcName, Structs, Enums);
    }

    private static void WriteProps(MetaBuilder builder, Span<byte> propInfo, IReadOnlyDictionary<PropSlot, IReadOnlyList<int>> props)
    {
        var anchors = props.Where(p => p.Value.Count > 0).OrderBy(p => p.Key).ToList();
        var total = anchors.Sum(a => a.Value.Count);
        if (total > 255) throw new InvalidDataException($"Too many props ({total}); a .ymt holds at most 255.");

        var metaData = new byte[total * 0x38];
        var n = 0;
        foreach (var (anchor, textureCounts) in anchors)
        {
            CheckCount(anchor.ToString(), textureCounts);
            for (var p = 0; p < textureCounts.Count; p++, n++)
            {
                var textures = new byte[textureCounts[p] * 0xC];
                for (var t = 0; t < textureCounts[p]; t++)
                {
                    textures[t * 0xC + 8] = (byte)t;
                    textures[t * 0xC + 0xB] = 0xFF;
                }
                var item = metaData.AsSpan(n * 0x38, 0x38);
                if (anchor == PropSlot.Hat) BinaryPrimitives.WriteUInt32LittleEndian(item[4..], HatHairExpression);
                MetaBuilder.WriteArray(item[0x18..], builder.Append(PropTextureData, textures), textureCounts[p]);
                item[0x32] = (byte)anchor;
                item[0x33] = (byte)p;
            }
        }
        propInfo[0] = (byte)total;
        MetaBuilder.WriteArray(propInfo[8..], total > 0 ? builder.Append(PropMetaData, metaData) : null, total);

        var anchorData = new byte[anchors.Count * 0x18];
        for (var i = 0; i < anchors.Count; i++)
        {
            var counts = anchors[i].Value.Select(c => (byte)c).ToArray();
            var item = anchorData.AsSpan(i * 0x18, 0x18);
            MetaBuilder.WriteArray(item, builder.Append(MetaBuilder.ByteType, counts), counts.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(item[0x10..], (uint)anchors[i].Key);
        }
        MetaBuilder.WriteArray(propInfo[0x18..], anchors.Count > 0 ? builder.Append(AnchorProps, anchorData) : null, anchors.Count);
    }

    private static void CheckCount(string slot, IReadOnlyList<int> textureCounts)
    {
        if (textureCounts.Count > MaxDrawablesPerSlot)
            throw new InvalidDataException($"{slot}: {textureCounts.Count} drawables; a .ymt holds at most {MaxDrawablesPerSlot} per slot.");
        foreach (var count in textureCounts)
            if (count is < 1 or > Drawable.MaxTextures)
                throw new InvalidDataException($"{slot}: a drawable has {count} textures; it needs 1 to {Drawable.MaxTextures}.");
    }

    public static PedVariation Read(ResourceFile resource)
    {
        var meta = new MetaReader(resource);
        var root = meta.Root;

        var components = new Dictionary<ComponentSlot, IReadOnlyList<int>>();
        var componentData = meta.Array(root[0x10..], 0x18, out _);
        for (var slot = 0; slot < ComponentSlots; slot++)
        {
            var index = root[4 + slot];
            if (index == 0xFF) continue;
            var drawables = meta.Array(componentData.Slice(index * 0x18 + 8), 0x30, out var count);
            var counts = new int[count];
            for (var d = 0; d < count; d++)
            {
                meta.Array(drawables.Slice(d * 0x30 + 8), 3, out var textures);
                counts[d] = textures;
            }
            components[(ComponentSlot)slot] = counts;
        }

        var props = new Dictionary<PropSlot, IReadOnlyList<int>>();
        var anchors = meta.Array(root[0x58..], 0x18, out var anchorCount);
        for (var i = 0; i < anchorCount; i++)
        {
            var item = anchors.Slice(i * 0x18, 0x18);
            var counts = meta.Array(item, 1, out _).ToArray().Select(b => (int)b).ToArray();
            props[(PropSlot)BinaryPrimitives.ReadUInt32LittleEndian(item[0x10..])] = counts;
        }

        return new PedVariation(BinaryPrimitives.ReadUInt32LittleEndian(root[0x68..]), components, props);
    }
}
