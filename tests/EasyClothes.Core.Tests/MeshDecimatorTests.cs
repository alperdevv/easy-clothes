using System.Numerics;
using EasyClothes.Core.Meshes;

namespace EasyClothes.Core.Tests;

public class MeshDecimatorTests
{
    [Fact]
    public void Flat_grid_is_reduced_and_keeps_its_border()
    {
        var (positions, indices) = Grid(12, z: (_, _) => 0);

        var result = MeshDecimator.Decimate(positions, indices, targetTriangles: 100);

        Assert.InRange(result.TriangleCount, 44, 100);
        var kept = result.KeptVertices.ToHashSet();
        Assert.All(BorderVertices(12), v => Assert.Contains(v, kept));
        AssertFacesUp(positions, result);
    }

    [Fact]
    public void Curved_surface_never_flips_triangles()
    {
        var (positions, indices) = Grid(16, z: (x, y) => 0.3f * MathF.Sin(x * 0.5f) * MathF.Cos(y * 0.4f));

        var result = MeshDecimator.Decimate(positions, indices, targetTriangles: indices.Length / 3 / 4);

        Assert.True(result.TriangleCount < indices.Length / 3 / 2);
        AssertFacesUp(positions, result);
    }

    [Fact]
    public void Target_above_current_count_changes_nothing()
    {
        var (positions, indices) = Grid(4, z: (_, _) => 0);

        var result = MeshDecimator.Decimate(positions, indices, targetTriangles: 1000);

        Assert.Equal(indices.Length / 3, result.TriangleCount);
    }

    private static void AssertFacesUp(float[] positions, DecimatedMesh result)
    {
        Vector3 P(int i)
        {
            var v = result.KeptVertices[result.Indices[i]];
            return new Vector3(positions[v * 3], positions[v * 3 + 1], positions[v * 3 + 2]);
        }
        for (var t = 0; t < result.Indices.Length; t += 3)
            Assert.True(Vector3.Cross(P(t + 1) - P(t), P(t + 2) - P(t)).Z > 0, $"triangle {t / 3} flipped");
    }

    private static (float[] Positions, ushort[] Indices) Grid(int n, Func<int, int, float> z)
    {
        var positions = new List<float>();
        for (var y = 0; y <= n; y++)
        for (var x = 0; x <= n; x++)
            positions.AddRange([x, y, z(x, y)]);
        var indices = new List<ushort>();
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            var i = (ushort)(y * (n + 1) + x);
            indices.AddRange([i, (ushort)(i + 1), (ushort)(i + n + 2), i, (ushort)(i + n + 2), (ushort)(i + n + 1)]);
        }
        return (positions.ToArray(), indices.ToArray());
    }

    private static IEnumerable<int> BorderVertices(int n) =>
        Enumerable.Range(0, (n + 1) * (n + 1)).Where(v => v % (n + 1) is 0 || v % (n + 1) == n || v / (n + 1) is 0 || v / (n + 1) == n);
}
