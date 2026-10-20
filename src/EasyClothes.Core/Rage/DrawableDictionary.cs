namespace EasyClothes.Core.Rage;

public enum LodLevel
{
    High,
    Medium,
    Low,
    VeryLow,
}

public sealed record GeometryInfo(int Vertices, int Indices, int Triangles, int VertexStride)
{
    public MeshData? Mesh { get; init; }
}

public sealed record LodInfo(LodLevel Level, IReadOnlyList<IReadOnlyList<GeometryInfo>> Models)
{
    public int Triangles => Models.Sum(m => m.Sum(g => g.Triangles));
    public int Vertices => Models.Sum(m => m.Sum(g => g.Vertices));
}

public sealed record DrawableInfo(string Name, IReadOnlyList<LodInfo> Lods);

public static class DrawableDictionary
{
    public const uint ResourceVersion = 165;

    private static readonly ulong[] DrawableLodLists = [0x50, 0x58, 0x60, 0x68];
    private const ulong DrawableName = 0xA8;

    private const ulong ModelGeometries = 0x08;

    private const ulong GeometryIndices = 0x58;
    private const ulong GeometryTriangles = 0x5C;
    private const ulong GeometryVertices = 0x60;
    private const ulong GeometryVertexBuffer = 0x18;
    private const ulong GeometryIndexBuffer = 0x38;
    private const ulong GeometryStride = 0x70;

    private const ulong VertexBufferCount = 0x18;
    private const ulong VertexBufferData = 0x20;
    private const ulong VertexBufferDeclaration = 0x30;

    private const ulong DeclarationFlags = 0x00;
    private const ulong DeclarationStride = 0x04;
    private const ulong DeclarationTypes = 0x08;

    private const ulong IndexBufferCount = 0x08;
    private const ulong IndexBufferData = 0x10;

    public static IReadOnlyList<DrawableInfo> Read(ResourceFile resource, bool includeMeshes = false)
    {
        if (resource.Header.Version != ResourceVersion)
            throw new InvalidDataException($"Expected a drawable dictionary (version {ResourceVersion}), got version {resource.Header.Version}.");

        var reader = new ResourceReader(resource);
        return reader.PointerArray(TextureDictionary.DictionaryRoot + TextureDictionary.DictionaryItems)
            .Select(drawable => ReadDrawable(reader, drawable, includeMeshes))
            .ToList();
    }

    private static DrawableInfo ReadDrawable(ResourceReader reader, ulong drawable, bool includeMeshes)
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
                        reader.UInt16(g + GeometryStride))
                    {
                        Mesh = includeMeshes ? ReadMesh(reader, g) : null,
                    })
                    .ToList())
                .ToList();
            lods.Add(new LodInfo((LodLevel)i, models));
        }

        return new DrawableInfo(reader.CString(reader.UInt64(drawable + DrawableName)) ?? "", lods);
    }

    private static MeshData ReadMesh(ResourceReader reader, ulong geometry)
    {
        var vb = reader.UInt64(geometry + GeometryVertexBuffer);
        var declPtr = reader.UInt64(vb + VertexBufferDeclaration);
        var decl = new VertexDeclaration(reader.UInt32(declPtr + DeclarationFlags), reader.UInt64(declPtr + DeclarationTypes));
        if (decl.Stride != reader.UInt16(declPtr + DeclarationStride))
            throw new InvalidDataException($"Vertex declaration 0x{decl.Flags:X} decodes to stride {decl.Stride}, file says {reader.UInt16(declPtr + DeclarationStride)}.");

        var vertexCount = (int)reader.UInt32(vb + VertexBufferCount);
        var vertices = reader.At(reader.UInt64(vb + VertexBufferData), vertexCount * decl.Stride);

        var ib = reader.UInt64(geometry + GeometryIndexBuffer);
        var indexCount = (int)reader.UInt32(ib + IndexBufferCount);
        var indices = reader.At(reader.UInt64(ib + IndexBufferData), indexCount * 2);

        return MeshData.Decode(vertices, vertexCount, decl, indices);
    }
}
