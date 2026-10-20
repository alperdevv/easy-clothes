using System.Numerics;

namespace EasyClothes.Core.Meshes;

public sealed record DecimatedMesh(int[] KeptVertices, int[] Indices)
{
    public int TriangleCount => Indices.Length / 3;
}

// Quadric-error half-edge collapse: every surviving vertex is an original one, so skinning
// weights and UVs never need interpolating. Boundary and UV-seam vertices are locked.
public static class MeshDecimator
{
    private const double MinNormalAgreement = 0.3;

    public static DecimatedMesh Decimate(float[] positions, IReadOnlyList<ushort> indices, int targetTriangles)
    {
        var vertexCount = positions.Length / 3;
        var tris = indices.Select(i => (int)i).ToArray();
        var triAlive = Enumerable.Repeat(true, tris.Length / 3).ToArray();
        var aliveCount = triAlive.Length;
        var vertTris = Enumerable.Range(0, vertexCount).Select(_ => new HashSet<int>()).ToArray();
        for (var t = 0; t < triAlive.Length; t++)
        {
            if (IsDegenerate(tris, t)) { triAlive[t] = false; aliveCount--; continue; }
            for (var k = 0; k < 3; k++) vertTris[tris[t * 3 + k]].Add(t);
        }

        var pos = Enumerable.Range(0, vertexCount).Select(v => new Vector3(positions[v * 3], positions[v * 3 + 1], positions[v * 3 + 2])).ToArray();
        var quadrics = BuildQuadrics(pos, tris, triAlive, vertexCount);
        var locked = FindLockedVertices(tris, triAlive, vertexCount);
        var removed = new bool[vertexCount];
        var version = new int[vertexCount];
        var queue = new PriorityQueue<(int U, int V, int Version), double>();

        void Schedule(int u)
        {
            if (locked[u] || removed[u]) return;
            if (BestTarget(u) is { } best) queue.Enqueue((u, best.V, version[u]), best.Cost);
        }

        (int V, double Cost)? BestTarget(int u)
        {
            (int V, double Cost)? best = null;
            foreach (var v in Neighbours(u))
            {
                if (!CanCollapse(u, v)) continue;
                var cost = Evaluate(quadrics[u] + quadrics[v], pos[v]);
                if (best is null || cost < best.Value.Cost) best = (v, cost);
            }
            return best;
        }

        IEnumerable<int> Neighbours(int u) =>
            vertTris[u].SelectMany(t => new[] { tris[t * 3], tris[t * 3 + 1], tris[t * 3 + 2] }).Where(w => w != u).Distinct();

        bool CanCollapse(int u, int v)
        {
            var shared = vertTris[u].Count(t => vertTris[v].Contains(t));
            if (shared == 0) return false;
            var common = Neighbours(u).Intersect(Neighbours(v)).Count();
            if (common != shared) return false;

            foreach (var t in vertTris[u])
            {
                if (vertTris[v].Contains(t)) continue;
                var before = Normal(pos[tris[t * 3]], pos[tris[t * 3 + 1]], pos[tris[t * 3 + 2]]);
                var after = Normal(Moved(t, 0), Moved(t, 1), Moved(t, 2));
                if (after == Vector3.Zero || Vector3.Dot(before, after) < MinNormalAgreement) return false;
            }
            return true;

            Vector3 Moved(int t, int k) => tris[t * 3 + k] == u ? pos[v] : pos[tris[t * 3 + k]];
        }

        for (var u = 0; u < vertexCount; u++) Schedule(u);

        while (aliveCount > targetTriangles && queue.TryDequeue(out var entry, out _))
        {
            var (u, v, ver) = entry;
            if (removed[u] || ver != version[u]) continue;
            if (removed[v] || !CanCollapse(u, v))
            {
                version[u]++;
                Schedule(u);
                continue;
            }

            var ring = Neighbours(u).Concat(Neighbours(v)).ToHashSet();
            foreach (var t in vertTris[u].ToList())
            {
                if (vertTris[v].Contains(t))
                {
                    triAlive[t] = false;
                    aliveCount--;
                    for (var k = 0; k < 3; k++) vertTris[tris[t * 3 + k]].Remove(t);
                }
                else
                {
                    for (var k = 0; k < 3; k++)
                        if (tris[t * 3 + k] == u) tris[t * 3 + k] = v;
                    vertTris[v].Add(t);
                }
            }
            vertTris[u].Clear();
            quadrics[v] += quadrics[u];
            removed[u] = true;

            foreach (var w in ring.Append(v))
            {
                version[w]++;
                Schedule(w);
            }
        }

        return Compact(tris, triAlive, vertexCount);
    }

    private static DecimatedMesh Compact(int[] tris, bool[] triAlive, int vertexCount)
    {
        var remap = Enumerable.Repeat(-1, vertexCount).ToArray();
        var kept = new List<int>();
        var indices = new List<int>();
        for (var t = 0; t < triAlive.Length; t++)
        {
            if (!triAlive[t]) continue;
            for (var k = 0; k < 3; k++)
            {
                var old = tris[t * 3 + k];
                if (remap[old] < 0)
                {
                    remap[old] = kept.Count;
                    kept.Add(old);
                }
                indices.Add(remap[old]);
            }
        }
        return new DecimatedMesh(kept.ToArray(), indices.ToArray());
    }

    private static Quadric[] BuildQuadrics(Vector3[] pos, int[] tris, bool[] triAlive, int vertexCount)
    {
        var quadrics = new Quadric[vertexCount];
        for (var t = 0; t < triAlive.Length; t++)
        {
            if (!triAlive[t]) continue;
            Vector3 a = pos[tris[t * 3]], b = pos[tris[t * 3 + 1]], c = pos[tris[t * 3 + 2]];
            var cross = Vector3.Cross(b - a, c - a);
            var area = cross.Length() / 2;
            if (area <= 0) continue;
            var n = Vector3.Normalize(cross);
            var plane = Quadric.FromPlane(n.X, n.Y, n.Z, -Vector3.Dot(n, a)) * area;
            for (var k = 0; k < 3; k++) quadrics[tris[t * 3 + k]] += plane;
        }
        return quadrics;
    }

    // Vertices on open or non-manifold edges stay put: garment openings and UV seams.
    private static bool[] FindLockedVertices(int[] tris, bool[] triAlive, int vertexCount)
    {
        var edgeUse = new Dictionary<(int, int), int>();
        for (var t = 0; t < triAlive.Length; t++)
        {
            if (!triAlive[t]) continue;
            for (var k = 0; k < 3; k++)
            {
                int a = tris[t * 3 + k], b = tris[t * 3 + (k + 1) % 3];
                var key = a < b ? (a, b) : (b, a);
                edgeUse[key] = edgeUse.GetValueOrDefault(key) + 1;
            }
        }
        var locked = new bool[vertexCount];
        foreach (var ((a, b), count) in edgeUse.Where(e => e.Value != 2))
            locked[a] = locked[b] = true;
        return locked;
    }

    private static bool IsDegenerate(int[] tris, int t) =>
        tris[t * 3] == tris[t * 3 + 1] || tris[t * 3 + 1] == tris[t * 3 + 2] || tris[t * 3] == tris[t * 3 + 2];

    private static Vector3 Normal(Vector3 a, Vector3 b, Vector3 c)
    {
        var cross = Vector3.Cross(b - a, c - a);
        return cross.LengthSquared() < 1e-14f ? Vector3.Zero : Vector3.Normalize(cross);
    }

    private static double Evaluate(Quadric q, Vector3 p) => q.Evaluate(p.X, p.Y, p.Z);
}

internal readonly record struct Quadric(double A2, double AB, double AC, double AD, double B2, double BC, double BD, double C2, double CD, double D2)
{
    public static Quadric FromPlane(double a, double b, double c, double d) =>
        new(a * a, a * b, a * c, a * d, b * b, b * c, b * d, c * c, c * d, d * d);

    public double Evaluate(double x, double y, double z) =>
        A2 * x * x + 2 * AB * x * y + 2 * AC * x * z + 2 * AD * x
        + B2 * y * y + 2 * BC * y * z + 2 * BD * y
        + C2 * z * z + 2 * CD * z + D2;

    public static Quadric operator +(Quadric p, Quadric q) =>
        new(p.A2 + q.A2, p.AB + q.AB, p.AC + q.AC, p.AD + q.AD, p.B2 + q.B2, p.BC + q.BC, p.BD + q.BD, p.C2 + q.C2, p.CD + q.CD, p.D2 + q.D2);

    public static Quadric operator *(Quadric q, double s) =>
        new(q.A2 * s, q.AB * s, q.AC * s, q.AD * s, q.B2 * s, q.BC * s, q.BD * s, q.C2 * s, q.CD * s, q.D2 * s);
}
