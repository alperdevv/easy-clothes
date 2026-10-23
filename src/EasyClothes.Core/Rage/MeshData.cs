using System.Buffers.Binary;
using System.Numerics;

namespace EasyClothes.Core.Rage;

public sealed class MeshData
{
    public required float[] Positions { get; init; }
    public required float[] Normals { get; init; }
    public required float[] TexCoords { get; init; }
    public required ushort[] Indices { get; init; }

    // Skinned meshes: four weights (summing to 255) and four skeleton bone indices per vertex, paired by position.
    public byte[]? BlendWeights { get; init; }
    public byte[]? BlendIndices { get; init; }

    public bool IsSkinned => BlendWeights is not null && BlendIndices is not null;

    public int VertexCount => Positions.Length / 3;
    public int TriangleCount => Indices.Length / 3;

    public MeshData Transformed(Matrix4x4 transform)
    {
        var positions = new float[Positions.Length];
        var normals = new float[Normals.Length];
        for (var i = 0; i < Positions.Length; i += 3)
        {
            var p = Vector3.Transform(new Vector3(Positions[i], Positions[i + 1], Positions[i + 2]), transform);
            var n = Vector3.TransformNormal(new Vector3(Normals[i], Normals[i + 1], Normals[i + 2]), transform);
            if (n != Vector3.Zero) n = Vector3.Normalize(n);
            p.CopyTo(positions, i);
            n.CopyTo(normals, i);
        }
        return new MeshData
        {
            Positions = positions, Normals = normals, TexCoords = TexCoords, Indices = Indices,
            BlendWeights = BlendWeights, BlendIndices = BlendIndices,
        };
    }

    // One mesh from several, e.g. the geometries of a model; skinning is kept only if every part has it.
    public static MeshData Merge(IReadOnlyList<MeshData> parts)
    {
        if (parts.Count == 0) throw new InvalidDataException("There is no geometry to merge.");
        var skinned = parts.All(p => p.IsSkinned);
        var offsets = new int[parts.Count];
        for (var i = 1; i < parts.Count; i++) offsets[i] = offsets[i - 1] + parts[i - 1].VertexCount;
        if (offsets[^1] + parts[^1].VertexCount > ushort.MaxValue)
            throw new InvalidDataException("The merged geometry has too many vertices for 16-bit indices.");
        return new MeshData
        {
            Positions = parts.SelectMany(p => p.Positions).ToArray(),
            Normals = parts.SelectMany(p => p.Normals).ToArray(),
            TexCoords = parts.SelectMany(p => p.TexCoords).ToArray(),
            Indices = parts.SelectMany((p, i) => p.Indices.Select(index => (ushort)(index + offsets[i]))).ToArray(),
            BlendWeights = skinned ? parts.SelectMany(p => p.BlendWeights!).ToArray() : null,
            BlendIndices = skinned ? parts.SelectMany(p => p.BlendIndices!).ToArray() : null,
        };
    }

    // OpenGL ES (ANGLE on Windows) has no polygon mode, so wireframe is drawn as a line list.
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

    // boneIds maps the geometry's bone slots to skeleton bones; the game's ped files use the identity.
    internal static MeshData Decode(ReadOnlySpan<byte> vertices, int count, VertexDeclaration decl, ReadOnlySpan<byte> indexBytes,
        IReadOnlyList<ushort>? boneIds = null)
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
        var skinned = decl.Has(VertexSemantic.BlendWeights) && decl.Has(VertexSemantic.BlendIndices);
        var weights = skinned ? new byte[count * 4] : null;
        var bones = skinned ? new byte[count * 4] : null;

        for (var v = 0; v < count; v++)
        {
            var vertex = vertices.Slice(v * decl.Stride, decl.Stride);
            for (var c = 0; c < 3; c++)
            {
                positions[v * 3 + c] = ReadFloat(vertex, pos + c * 4);
                if (nrm >= 0) normals[v * 3 + c] = ReadFloat(vertex, nrm + c * 4);
            }
            if (skinned)
            {
                vertex.Slice(decl.OffsetOf(VertexSemantic.BlendWeights), 4).CopyTo(weights.AsSpan(v * 4));
                for (var k = 0; k < 4; k++)
                {
                    var slot = vertex[decl.OffsetOf(VertexSemantic.BlendIndices) + k];
                    bones![v * 4 + k] = boneIds is { } ids && slot < ids.Count ? (byte)Math.Min(ids[slot], (ushort)255) : slot;
                }
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

        return new MeshData { Positions = positions, Normals = normals, TexCoords = uvs, Indices = indices, BlendWeights = weights, BlendIndices = bones };
    }

    private static float ReadFloat(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
}
