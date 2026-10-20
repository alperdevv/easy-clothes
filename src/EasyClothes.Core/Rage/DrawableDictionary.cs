namespace EasyClothes.Core.Rage;

public enum LodLevel
{
    High,
    Medium,
    Low,
    VeryLow,
}

public sealed record GeometryInfo(int Vertices, int Indices, int Triangles, int VertexStride);

public sealed record LodInfo(LodLevel Level, IReadOnlyList<IReadOnlyList<GeometryInfo>> Models)
{
    public int Triangles => Models.Sum(m => m.Sum(g => g.Triangles));
    public int Vertices => Models.Sum(m => m.Sum(g => g.Vertices));
}

public sealed record DrawableInfo(string Name, IReadOnlyList<LodInfo> Lods);

/// <summary>Reads the drawables of a .ydd (pgDictionary&lt;gtaDrawable&gt;): names, LODs and mesh sizes.</summary>
public static class DrawableDictionary
{
    public const uint ResourceVersion = 165;

    // Offsets inside gtaDrawable.
    private static readonly ulong[] DrawableLodLists = [0x50, 0x58, 0x60, 0x68];
    private const ulong DrawableName = 0xA8;

    // Offsets inside grmModel.
    private const ulong ModelGeometries = 0x08;

    // Offsets inside grmGeometryQB.
    private const ulong GeometryIndices = 0x58;
    private const ulong GeometryTriangles = 0x5C;
    private const ulong GeometryVertices = 0x60;
    private const ulong GeometryStride = 0x70;

    public static IReadOnlyList<DrawableInfo> Read(ResourceFile resource)
    {
        if (resource.Header.Version != ResourceVersion)
            throw new InvalidDataException($"Expected a drawable dictionary (version {ResourceVersion}), got version {resource.Header.Version}.");

        var reader = new ResourceReader(resource);
        return reader.PointerArray(TextureDictionary.DictionaryRoot + TextureDictionary.DictionaryItems)
            .Select(drawable => ReadDrawable(reader, drawable))
            .ToList();
    }

    private static DrawableInfo ReadDrawable(ResourceReader reader, ulong drawable)
    {
        var lods = new List<LodInfo>();
        for (var i = 0; i < DrawableLodLists.Length; i++)
        {
            var list = reader.UInt64(drawable + DrawableLodLists[i]);
            if (list == 0) continue;

            var models = reader.PointerArray(list)
                .Select(model => (IReadOnlyList<GeometryInfo>)reader.PointerArray(model + ModelGeometries)
                    .Select(g => new GeometryInfo(
                        reader.UInt16(g + GeometryVertices),
                        (int)reader.UInt32(g + GeometryIndices),
                        (int)reader.UInt32(g + GeometryTriangles),
                        reader.UInt16(g + GeometryStride)))
                    .ToList())
                .ToList();
            lods.Add(new LodInfo((LodLevel)i, models));
        }

        return new DrawableInfo(reader.CString(reader.UInt64(drawable + DrawableName)) ?? "", lods);
    }
}
