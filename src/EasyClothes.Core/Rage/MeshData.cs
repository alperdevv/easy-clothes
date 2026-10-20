using System.Buffers.Binary;

namespace EasyClothes.Core.Rage;

/// <summary>
/// One geometry's render data in a GPU-friendly layout: flat float arrays
/// (positions xyz, normals xyz, uvs uv) and 16-bit triangle-list indices.
/// </summary>
public sealed class MeshData
{
    public required float[] Positions { get; init; }
    public required float[] Normals { get; init; }
    public required float[] TexCoords { get; init; }
    public required ushort[] Indices { get; init; }

    public int VertexCount => Positions.Length / 3;
    public int TriangleCount => Indices.Length / 3;

    /// <summary>
    /// Unique triangle edges as a line-list index buffer. Used for wireframe rendering,
    /// since OpenGL ES (ANGLE on Windows) has no polygon mode.
    /// </summary>
    public ushort[] WireframeIndices()
    {
        var edges = new HashSet<uint>();
        var lines = new List<ushort>(Indices.Length * 2);
        for (var t = 0; t + 2 < Indices.Length; t += 3)
        {
            for (var e = 0; e < 3; e++)
            {
                var a = Indices[t + e];
                var b = Indices[t + (e + 1) % 3];
                var key = a < b ? (uint)a << 16 | b : (uint)b << 16 | a;
                if (!edges.Add(key)) continue;
                lines.Add(a);
                lines.Add(b);
            }
        }
        return lines.ToArray();
    }

    internal static MeshData Decode(ReadOnlySpan<byte> vertices, int count, VertexDeclaration decl, ReadOnlySpan<byte> indexBytes)
    {
        if (!decl.Has(VertexSemantic.Position) || decl.TypeOf(VertexSemantic.Position) != VertexComponentType.Float3)
            throw new InvalidDataException("Geometry has no Float3 position stream.");

        var positions = new float[count * 3];
        var normals = new float[count * 3];
        var uvs = new float[count * 2];
        var pos = decl.OffsetOf(VertexSemantic.Position);
        var nrm = decl.TypeOf(VertexSemantic.Normal) == VertexComponentType.Float3 ? decl.OffsetOf(VertexSemantic.Normal) : -1;
        var uv = decl.OffsetOf(VertexSemantic.TexCoord0);
        var uvType = uv >= 0 ? decl.TypeOf(VertexSemantic.TexCoord0) : default;

        for (var v = 0; v < count; v++)
        {
            var vertex = vertices.Slice(v * decl.Stride, decl.Stride);
            for (var c = 0; c < 3; c++)
            {
                positions[v * 3 + c] = ReadFloat(vertex, pos + c * 4);
                if (nrm >= 0) normals[v * 3 + c] = ReadFloat(vertex, nrm + c * 4);
            }
            switch (uvType)
            {
                case VertexComponentType.Float2:
                    uvs[v * 2] = ReadFloat(vertex, uv);
                    uvs[v * 2 + 1] = ReadFloat(vertex, uv + 4);
                    break;
                case VertexComponentType.Half2:
                    uvs[v * 2] = (float)BinaryPrimitives.ReadHalfLittleEndian(vertex[uv..]);
                    uvs[v * 2 + 1] = (float)BinaryPrimitives.ReadHalfLittleEndian(vertex[(uv + 2)..]);
                    break;
            }
        }

        var indices = new ushort[indexBytes.Length / 2];
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = BinaryPrimitives.ReadUInt16LittleEndian(indexBytes[(i * 2)..]);
            if (indices[i] >= count)
                throw new InvalidDataException($"Index {indices[i]} out of range for {count} vertices.");
        }

        return new MeshData { Positions = positions, Normals = normals, TexCoords = uvs, Indices = indices };
    }

    private static float ReadFloat(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
}
