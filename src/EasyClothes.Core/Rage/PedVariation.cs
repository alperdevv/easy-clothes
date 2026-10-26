using System.Buffers.Binary;
using EasyClothes.Core.Model;
using EasyClothes.Core.Rage.Meta;
using static EasyClothes.Core.Rage.Meta.PedVariationSchema;

namespace EasyClothes.Core.Rage;

// One drawable as the .ymt sees it. HeelHeight only matters for shoes and HairScale for hats.
// Alternates is the highest "_k" model streamed next to it; PropMask leaves out 0x10, which is Skin.
public sealed record VariationDrawable(int Textures)
{
    public int Alternates { get; init; }
    public int PropMask { get; init; } = 1;
    public bool Skin { get; init; }
    public bool Cloth { get; init; }
    public float HeelHeight { get; init; }
    public float HairScale { get; init; }
}

// Contents of a ped variation .ymt: the drawables of each component slot and prop anchor, in drawable order.
public sealed record PedVariation(
    uint DlcNameHash,
    IReadOnlyDictionary<ComponentSlot, IReadOnlyList<VariationDrawable>> Components,
    IReadOnlyDictionary<PropSlot, IReadOnlyList<VariationDrawable>> Props)
{
    public const int MaxDrawablesPerSlot = 255;
    private const int ComponentSlots = 12;
    private static readonly uint AudioNone = Jenkins.Hash("none");
    private static readonly uint AudioHeels = Jenkins.Hash("shoe_heels");

    public static ResourceFile Write(string dlcName,
        IReadOnlyDictionary<ComponentSlot, IReadOnlyList<VariationDrawable>> components,
        IReadOnlyDictionary<PropSlot, IReadOnlyList<VariationDrawable>> props)
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
            var (slot, items) = (usedSlots[i].Key, usedSlots[i].Value);
            CheckCount(slot.ToString(), items);
            root[4 + (int)slot] = (byte)i;

            var drawables = new byte[items.Count * 0x30];
            for (var d = 0; d < items.Count; d++)
            {
                var textures = new byte[items[d].Textures * 3];
                for (var t = 0; t < items[d].Textures; t++)
                {
                    textures[t * 3] = (byte)(items[d].Skin ? 1 : 0); // texId: "_whi" instead of "_uni"
                    textures[t * 3 + 1] = 0xFF;
                }
                var item = drawables.AsSpan(d * 0x30, 0x30);
                item[0] = (byte)(items[d].PropMask & ~0x10 | (items[d].Skin ? 0x10 : 0)); // propMask: 0x10 is skin-toned ("_r")
                item[1] = (byte)items[d].Alternates; // numAlternatives
                item[0x18] = (byte)(items[d].Cloth ? 1 : 0);
                MetaBuilder.WriteArray(item[8..], builder.Append(TextureData, textures), items[d].Textures);
            }
            var entry = componentData.AsSpan(i * 0x18, 0x18);
            entry[0] = (byte)Math.Min(255, items.Sum(d => d.Textures));
            MetaBuilder.WriteArray(entry[8..], builder.Append(DrawableData, drawables), items.Count);
        }
        MetaBuilder.WriteArray(root.AsSpan(0x10), usedSlots.Count > 0 ? builder.Append(ComponentData, componentData) : null, usedSlots.Count);

        var infos = usedSlots.SelectMany(s => s.Value.Select((v, d) => (Slot: s.Key, Drawable: d, v.HeelHeight))).ToList();
        var componentInfos = new byte[infos.Count * 0x30];
        for (var i = 0; i < infos.Count; i++)
        {
            var item = componentInfos.AsSpan(i * 0x30, 0x30);
            var heels = infos[i].HeelHeight > 0;
            BinaryPrimitives.WriteUInt32LittleEndian(item, heels ? AudioHeels : AudioNone);
            if (heels) BinaryPrimitives.WriteSingleLittleEndian(item[0x18..], infos[i].HeelHeight); // expressionMods[4]
            item[0x2C] = (byte)infos[i].Slot;
            item[0x2D] = (byte)infos[i].Drawable;
        }
        MetaBuilder.WriteArray(root.AsSpan(0x30), infos.Count > 0 ? builder.Append(ComponentInfo, componentInfos) : null, infos.Count);

        WriteProps(builder, root.AsSpan(0x40, 0x28), props);
        BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(0x68), Jenkins.Hash(dlcName));
        builder.Patch(rootAt, root);

        return builder.Build(dlcName, Structs, Enums);
    }

    private static void WriteProps(MetaBuilder builder, Span<byte> propInfo, IReadOnlyDictionary<PropSlot, IReadOnlyList<VariationDrawable>> props)
    {
        var anchors = props.Where(p => p.Value.Count > 0).OrderBy(p => p.Key).ToList();
        var total = anchors.Sum(a => a.Value.Count);
        if (total > 255) throw new InvalidDataException($"Too many props ({total}); a .ymt holds at most 255.");

        var metaData = new byte[total * 0x38];
        var n = 0;
        foreach (var (anchor, items) in anchors)
        {
            CheckCount(anchor.ToString(), items);
            for (var p = 0; p < items.Count; p++, n++)
            {
                var textures = new byte[items[p].Textures * 0xC];
                for (var t = 0; t < items[p].Textures; t++)
                {
                    textures[t * 0xC + 8] = (byte)t;
                    textures[t * 0xC + 0xB] = 0xFF;
                }
                var item = metaData.AsSpan(n * 0x38, 0x38);
                BinaryPrimitives.WriteSingleLittleEndian(item[4..], items[p].HairScale); // expressionMods[0]
                MetaBuilder.WriteArray(item[0x18..], builder.Append(PropTextureData, textures), items[p].Textures);
                item[0x32] = (byte)anchor;
                item[0x33] = (byte)p;
            }
        }
        propInfo[0] = (byte)total;
        MetaBuilder.WriteArray(propInfo[8..], total > 0 ? builder.Append(PropMetaData, metaData) : null, total);

        var anchorData = new byte[anchors.Count * 0x18];
        for (var i = 0; i < anchors.Count; i++)
        {
            var counts = anchors[i].Value.Select(c => (byte)c.Textures).ToArray();
            var item = anchorData.AsSpan(i * 0x18, 0x18);
            MetaBuilder.WriteArray(item, builder.Append(MetaBuilder.ByteType, counts), counts.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(item[0x10..], (uint)anchors[i].Key);
        }
        MetaBuilder.WriteArray(propInfo[0x18..], anchors.Count > 0 ? builder.Append(AnchorProps, anchorData) : null, anchors.Count);
    }

    private static void CheckCount(string slot, IReadOnlyList<VariationDrawable> items)
    {
        if (items.Count > MaxDrawablesPerSlot)
            throw new InvalidDataException($"{slot}: {items.Count} drawables; a .ymt holds at most {MaxDrawablesPerSlot} per slot.");
        foreach (var item in items)
            if (item.Textures is < 1 or > Drawable.MaxTextures)
                throw new InvalidDataException($"{slot}: a drawable has {item.Textures} textures; it needs 1 to {Drawable.MaxTextures}.");
    }

    public static PedVariation Read(ResourceFile resource)
    {
        var meta = new MetaReader(resource);
        var root = meta.Root;

        var heels = new Dictionary<(int Slot, int Drawable), float>();
        var infos = meta.Array(root[0x30..], 0x30, out var infoCount);
        for (var i = 0; i < infoCount; i++)
        {
            var item = infos.Slice(i * 0x30, 0x30);
            heels[(item[0x2C], item[0x2D])] = BinaryPrimitives.ReadSingleLittleEndian(item[0x18..]);
        }

        var components = new Dictionary<ComponentSlot, IReadOnlyList<VariationDrawable>>();
        var componentData = meta.Array(root[0x10..], 0x18, out _);
        for (var slot = 0; slot < ComponentSlots; slot++)
        {
            var index = root[4 + slot];
            if (index == 0xFF) continue;
            var drawables = meta.Array(componentData.Slice(index * 0x18 + 8), 0x30, out var count);
            var items = new VariationDrawable[count];
            for (var d = 0; d < count; d++)
            {
                var item = drawables.Slice(d * 0x30, 0x30);
                meta.Array(item[8..], 3, out var textures);
                items[d] = new VariationDrawable(textures)
                {
                    Alternates = item[1],
                    PropMask = item[0] & ~0x10,
                    Skin = (item[0] & 0x10) != 0,
                    Cloth = item[0x18] > 0,
                    HeelHeight = heels.GetValueOrDefault((slot, d)),
                };
            }
            components[(ComponentSlot)slot] = items;
        }

        var hair = new Dictionary<(int Anchor, int Prop), float>();
        var propData = meta.Array(root[0x48..], 0x38, out var propCount);
        for (var i = 0; i < propCount; i++)
        {
            var item = propData.Slice(i * 0x38, 0x38);
            hair[(item[0x32], item[0x33])] = BinaryPrimitives.ReadSingleLittleEndian(item[4..]);
        }

        var props = new Dictionary<PropSlot, IReadOnlyList<VariationDrawable>>();
        var anchors = meta.Array(root[0x58..], 0x18, out var anchorCount);
        for (var i = 0; i < anchorCount; i++)
        {
            var item = anchors.Slice(i * 0x18, 0x18);
            var anchor = (int)BinaryPrimitives.ReadUInt32LittleEndian(item[0x10..]);
            props[(PropSlot)anchor] = meta.Array(item, 1, out _).ToArray()
                .Select((textures, p) => new VariationDrawable(textures) { HairScale = hair.GetValueOrDefault((anchor, p)) })
                .ToArray();
        }

        return new PedVariation(BinaryPrimitives.ReadUInt32LittleEndian(root[0x68..]), components, props);
    }
}
