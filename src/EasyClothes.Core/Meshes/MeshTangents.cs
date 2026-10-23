using System.Numerics;
using EasyClothes.Core.Rage;

namespace EasyClothes.Core.Meshes;

public static class MeshTangents
{
    // Per-vertex tangent along +U; W is the handedness sign with the game's convention (checked on real props).
    public static Vector4[] Compute(MeshData mesh)
    {
        var count = mesh.VertexCount;
        var tangents = new Vector3[count];
        var bitangents = new Vector3[count];
        for (var t = 0; t + 2 < mesh.Indices.Length; t += 3)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
            Vector3 pa = Position(mesh, a), e1 = Position(mesh, b) - pa, e2 = Position(mesh, c) - pa;
            Vector2 ua = Uv(mesh, a), d1 = Uv(mesh, b) - ua, d2 = Uv(mesh, c) - ua;
            var det = d1.X * d2.Y - d2.X * d1.Y;
            if (MathF.Abs(det) < 1e-12f) continue;
            var tangent = (e1 * d2.Y - e2 * d1.Y) / det;
            var bitangent = (e2 * d1.X - e1 * d2.X) / det;
            foreach (var v in (ReadOnlySpan<int>)[a, b, c])
            {
                tangents[v] += tangent;
                bitangents[v] += bitangent;
            }
        }

        var result = new Vector4[count];
        for (var v = 0; v < count; v++)
        {
            var normal = new Vector3(mesh.Normals[v * 3], mesh.Normals[v * 3 + 1], mesh.Normals[v * 3 + 2]);
            var tangent = tangents[v] - normal * Vector3.Dot(normal, tangents[v]);
            if (tangent.LengthSquared() < 1e-12f) tangent = Perpendicular(normal);
            tangent = Vector3.Normalize(tangent);
            var handedness = Vector3.Dot(Vector3.Cross(normal, tangent), bitangents[v]) < 0 ? 1f : -1f;
            result[v] = new Vector4(tangent, handedness);
        }
        return result;
    }

    private static Vector3 Position(MeshData mesh, int v) => new(mesh.Positions[v * 3], mesh.Positions[v * 3 + 1], mesh.Positions[v * 3 + 2]);

    private static Vector2 Uv(MeshData mesh, int v) => new(mesh.TexCoords[v * 2], mesh.TexCoords[v * 2 + 1]);

    private static Vector3 Perpendicular(Vector3 n) =>
        MathF.Abs(n.X) < 0.9f ? Vector3.Cross(n, Vector3.UnitX) : Vector3.Cross(n, Vector3.UnitY);
}
