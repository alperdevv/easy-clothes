using System.Buffers.Binary;
using EasyClothes.Core.Rage;

namespace EasyClothes.Core.Meshes;

public sealed record MeshReduction(int OldTriangles, int NewTriangles, int OldVertices, int NewVertices);

public static class MeshReducer
{
    // Reduces the high LOD in place: the smaller buffers are written over the originals and the
    // leftover bytes zeroed, so the resource layout (and every pointer in it) stays untouched.
    public static MeshReduction Reduce(string path, int targetTriangles)
    {
        var resource = ResourceFile.Read(path);
        var reader = new ResourceReader(resource);
        var geometries = DrawableDictionary.Read(resource, includeMeshes: true)
            .SelectMany(d => d.Lods.Where(l => l.Level == LodLevel.High))
            .SelectMany(l => l.Models).SelectMany(m => m)
            .ToList();

        var oldTriangles = geometries.Sum(g => g.Triangles);
        var oldVertices = geometries.Sum(g => g.Vertices);
        var ratio = Math.Min(1.0, (double)targetTriangles / Math.Max(1, oldTriangles));
        int newTriangles = 0, newVertices = 0;

        foreach (var geometry in geometries)
        {
            var mesh = geometry.Mesh!;
            var target = (int)Math.Ceiling(mesh.TriangleCount * ratio);
            var result = MeshDecimator.Decimate(mesh.Positions, mesh.Indices, target);
            Patch(reader, geometry, result);
            newTriangles += result.TriangleCount;
            newVertices += result.KeptVertices.Length;
        }

        resource.Write(path);
        return new MeshReduction(oldTriangles, newTriangles, oldVertices, newVertices);
    }

    private static void Patch(ResourceReader reader, GeometryInfo geometry, DecimatedMesh result)
    {
        var g = geometry.Pointer;
        var stride = geometry.VertexStride;

        var vb = reader.UInt64(g + DrawableDictionary.GeometryVertexBuffer);
        var vertexData = reader.Writable(reader.UInt64(vb + DrawableDictionary.VertexBufferData), geometry.Vertices * stride);
        var original = vertexData.ToArray();
        vertexData.Clear();
        for (var i = 0; i < result.KeptVertices.Length; i++)
            original.AsSpan(result.KeptVertices[i] * stride, stride).CopyTo(vertexData[(i * stride)..]);

        var ib = reader.UInt64(g + DrawableDictionary.GeometryIndexBuffer);
        var indexData = reader.Writable(reader.UInt64(ib + DrawableDictionary.IndexBufferData), geometry.Indices * 2);
        indexData.Clear();
        for (var i = 0; i < result.Indices.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(indexData[(i * 2)..], (ushort)result.Indices[i]);

        BinaryPrimitives.WriteUInt32LittleEndian(reader.Writable(vb + DrawableDictionary.VertexBufferCount, 4), (uint)result.KeptVertices.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(reader.Writable(ib + DrawableDictionary.IndexBufferCount, 4), (uint)result.Indices.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(reader.Writable(g + DrawableDictionary.GeometryIndices, 4), (uint)result.Indices.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(reader.Writable(g + DrawableDictionary.GeometryTriangles, 4), (uint)result.TriangleCount);
        BinaryPrimitives.WriteUInt16LittleEndian(reader.Writable(g + DrawableDictionary.GeometryVertices, 2), (ushort)result.KeptVertices.Length);
    }
}
