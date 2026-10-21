using System.Numerics;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Viewing;

public sealed record RenderItem(IReadOnlyList<MeshData> Meshes, RgbaImage? Texture);

// CPU rasteriser with the same shading as the GL viewer, for thumbnails and headless checks.
public static class SoftwareRenderer
{
    private static readonly Vector3 Untextured = new(0.72f);

    public static RgbaImage Render(IReadOnlyList<RenderItem> items, OrbitCamera camera, int width, int height, uint backgroundRgba = 0)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(backgroundRgba >> 24);
            pixels[i + 1] = (byte)(backgroundRgba >> 16);
            pixels[i + 2] = (byte)(backgroundRgba >> 8);
            pixels[i + 3] = (byte)backgroundRgba;
        }
        var depth = new float[width * height];
        Array.Fill(depth, float.MaxValue);

        var viewProjection = camera.ViewMatrix * camera.ProjectionMatrix((float)width / height);
        var light = Vector3.Normalize(camera.Position - camera.Target);
        var target = new Target(pixels, depth, width, height);

        foreach (var item in items)
        {
            foreach (var mesh in item.Meshes)
            {
                var projected = Project(mesh, viewProjection, width, height);
                for (var t = 0; t + 2 < mesh.Indices.Length; t += 3)
                    DrawTriangle(target, mesh, projected, mesh.Indices[t], mesh.Indices[t + 1], mesh.Indices[t + 2], item.Texture, light);
            }
        }
        return new RgbaImage(width, height, pixels);
    }

    private readonly record struct Target(byte[] Pixels, float[] Depth, int Width, int Height);

    // X, Y in pixels, Z in NDC, W = 1/w for perspective-correct interpolation (0 when behind the camera).
    private static Vector4[] Project(MeshData mesh, Matrix4x4 viewProjection, int width, int height)
    {
        var result = new Vector4[mesh.VertexCount];
        for (var v = 0; v < result.Length; v++)
        {
            var clip = Vector4.Transform(new Vector3(mesh.Positions[v * 3], mesh.Positions[v * 3 + 1], mesh.Positions[v * 3 + 2]), viewProjection);
            if (clip.W <= 1e-5f) continue;
            var invW = 1 / clip.W;
            result[v] = new Vector4(
                (clip.X * invW + 1) * 0.5f * width,
                (1 - clip.Y * invW) * 0.5f * height,
                clip.Z * invW,
                invW);
        }
        return result;
    }

    private static void DrawTriangle(Target target, MeshData mesh, Vector4[] projected, int a, int b, int c, RgbaImage? texture, Vector3 light)
    {
        var (pa, pb, pc) = (projected[a], projected[b], projected[c]);
        if (pa.W == 0 || pb.W == 0 || pc.W == 0) return;
        var area = Edge(pa, pb, pc.X, pc.Y);
        if (MathF.Abs(area) < 1e-8f) return;

        var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(pa.X, MathF.Min(pb.X, pc.X))));
        var maxX = Math.Min(target.Width - 1, (int)MathF.Ceiling(MathF.Max(pa.X, MathF.Max(pb.X, pc.X))));
        var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(pa.Y, MathF.Min(pb.Y, pc.Y))));
        var maxY = Math.Min(target.Height - 1, (int)MathF.Ceiling(MathF.Max(pa.Y, MathF.Max(pb.Y, pc.Y))));

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var (px, py) = (x + 0.5f, y + 0.5f);
                var wa = Edge(pb, pc, px, py) / area;
                var wb = Edge(pc, pa, px, py) / area;
                var wc = 1 - wa - wb;
                if (wa < 0 || wb < 0 || wc < 0) continue;

                var z = wa * pa.Z + wb * pb.Z + wc * pc.Z;
                var index = y * target.Width + x;
                if (z < -1 || z > 1 || z >= target.Depth[index]) continue;

                var invW = wa * pa.W + wb * pb.W + wc * pc.W;
                var (ka, kb, kc) = (wa * pa.W / invW, wb * pb.W / invW, wc * pc.W / invW);
                var colour = Untextured;
                if (texture is not null)
                {
                    var u = ka * mesh.TexCoords[a * 2] + kb * mesh.TexCoords[b * 2] + kc * mesh.TexCoords[c * 2];
                    var v = ka * mesh.TexCoords[a * 2 + 1] + kb * mesh.TexCoords[b * 2 + 1] + kc * mesh.TexCoords[c * 2 + 1];
                    var texel = Sample(texture, u, v);
                    if (texel.W < 0.33f) continue;
                    colour = new Vector3(texel.X, texel.Y, texel.Z);
                }

                var normal = Vector3.Normalize(ka * Normal(mesh, a) + kb * Normal(mesh, b) + kc * Normal(mesh, c));
                var shade = 0.35f + 0.65f * MathF.Abs(Vector3.Dot(normal, light));
                if (float.IsNaN(shade)) shade = 0.35f;

                target.Depth[index] = z;
                var o = index * 4;
                target.Pixels[o] = (byte)Math.Clamp(colour.X * shade * 255, 0, 255);
                target.Pixels[o + 1] = (byte)Math.Clamp(colour.Y * shade * 255, 0, 255);
                target.Pixels[o + 2] = (byte)Math.Clamp(colour.Z * shade * 255, 0, 255);
                target.Pixels[o + 3] = 255;
            }
        }
    }

    private static float Edge(Vector4 a, Vector4 b, float x, float y) => (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);

    private static Vector3 Normal(MeshData mesh, int v) => new(mesh.Normals[v * 3], mesh.Normals[v * 3 + 1], mesh.Normals[v * 3 + 2]);

    private static Vector4 Sample(RgbaImage image, float u, float v)
    {
        var x = (int)MathF.Floor((u - MathF.Floor(u)) * image.Width) % image.Width;
        var y = (int)MathF.Floor((v - MathF.Floor(v)) * image.Height) % image.Height;
        var o = (y * image.Width + x) * 4;
        return new Vector4(image.Pixels[o], image.Pixels[o + 1], image.Pixels[o + 2], image.Pixels[o + 3]) / 255f;
    }
}
