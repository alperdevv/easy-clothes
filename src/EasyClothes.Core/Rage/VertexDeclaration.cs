namespace EasyClothes.Core.Rage;

/// <summary>Vertex semantics, in the bit order used by <see cref="VertexDeclaration.Flags"/>.</summary>
public enum VertexSemantic
{
    Position = 0,
    BlendWeights = 1,
    BlendIndices = 2,
    Normal = 3,
    Colour0 = 4,
    Colour1 = 5,
    TexCoord0 = 6,
    TexCoord1 = 7,
    TexCoord2 = 8,
    TexCoord3 = 9,
    TexCoord4 = 10,
    TexCoord5 = 11,
    TexCoord6 = 12,
    TexCoord7 = 13,
    Tangent = 14,
    Binormal = 15,
}

/// <summary>Storage type of one vertex component (4-bit codes in <see cref="VertexDeclaration.Types"/>).</summary>
public enum VertexComponentType
{
    Half2 = 1,
    Float = 2,
    Half4 = 3,
    Float2 = 5,
    Float3 = 6,
    Float4 = 7,
    UByte4 = 8,
    Colour = 9,
    Dec3N = 10,
}

/// <summary>
/// grcVertexDeclaration: a bitmask of present semantics plus a 4-bit type per semantic.
/// Components are packed in semantic order with no padding.
/// </summary>
public sealed class VertexDeclaration
{
    private readonly int[] _offsets = new int[16];

    public uint Flags { get; }
    public ulong Types { get; }
    public int Stride { get; }

    public VertexDeclaration(uint flags, ulong types)
    {
        Flags = flags;
        Types = types;
        var offset = 0;
        for (var i = 0; i < 16; i++)
        {
            _offsets[i] = -1;
            if ((flags & (1u << i)) == 0) continue;
            _offsets[i] = offset;
            offset += SizeOf(TypeOf((VertexSemantic)i));
        }
        Stride = offset;
    }

    public bool Has(VertexSemantic semantic) => _offsets[(int)semantic] >= 0;

    /// <summary>Byte offset of the semantic inside a vertex, or -1 when absent.</summary>
    public int OffsetOf(VertexSemantic semantic) => _offsets[(int)semantic];

    public VertexComponentType TypeOf(VertexSemantic semantic) =>
        (VertexComponentType)((Types >> (4 * (int)semantic)) & 0xF);

    public static int SizeOf(VertexComponentType type) => type switch
    {
        VertexComponentType.Half2 or VertexComponentType.Float or VertexComponentType.UByte4
            or VertexComponentType.Colour or VertexComponentType.Dec3N => 4,
        VertexComponentType.Half4 or VertexComponentType.Float2 => 8,
        VertexComponentType.Float3 => 12,
        VertexComponentType.Float4 => 16,
        _ => throw new InvalidDataException($"Unknown vertex component type {(int)type}."),
    };
}
