using System.Numerics;
using EasyClothes.Core.Rage;

namespace EasyClothes.Core.Modeling;

public enum CapZone
{
    Crown,
    Rim,
    Inner,
    Visor,
    UnderVisor,
    VisorEdge,
    Button,
    Band,
    Cuff,
    Pompom,
}

// What the texture painter needs at a vertex: its zone, how far round the piece it lies (Along,
// 0..1 around a crown) and how far across it (Across, 0 at the attached edge, 1 at the far one).
public readonly record struct Paint(CapZone Zone, float Along, float Across);

// The mesh as seen by the texture painter: triangles in UV space with paint values at the corners.
public sealed record PaintMesh(Vector2[] Uvs, Paint[] Paints, ushort[] Indices);

// Collects triangles in parts; normals are smoothed within a part only, so edges between parts stay sharp.
internal sealed class MeshBuilder
{
    private readonly List<Vector3> _positions = [];
    private readonly List<Vector2> _uvs = [];
    private readonly List<Paint> _paints = [];
    private readonly List<int> _parts = [];
    private readonly List<ushort> _indices = [];
    private int _partCount;

    public int NewPart() => _partCount++;

    public int Add(int part, Vector3 position, Vector2 uv, Paint paint)
    {
        if (_positions.Count == ushort.MaxValue) throw new InvalidOperationException("Mesh has too many vertices for 16-bit indices.");
        _positions.Add(position);
        _uvs.Add(uv);
        _paints.Add(paint);
        _parts.Add(part);
        return _positions.Count - 1;
    }

    public Vector3 PositionOf(int vertex) => _positions[vertex];

    // Counter-clockwise seen from the side the face looks at, like the game's models.
    public void Triangle(int a, int b, int c)
    {
        _indices.Add((ushort)a);
        _indices.Add((ushort)b);
        _indices.Add((ushort)c);
    }

    public void Quad(int a, int b, int c, int d)
    {
        Triangle(a, b, c);
        Triangle(a, c, d);
    }

    public PaintMesh ToPaintMesh() => new(_uvs.ToArray(), _paints.ToArray(), _indices.ToArray());

    public MeshData ToMesh(Func<Vector3, Vector3> transform)
    {
        var positions = _positions.Select(transform).ToArray();
        var normals = new Vector3[positions.Length];
        var welded = new Dictionary<(int, Vector3), Vector3>();
        Vector3 Key(int v) => new(MathF.Round(positions[v].X, 5), MathF.Round(positions[v].Y, 5), MathF.Round(positions[v].Z, 5));
        for (var t = 0; t < _indices.Count; t += 3)
        {
            var (a, b, c) = (_indices[t], _indices[t + 1], _indices[t + 2]);
            var face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            foreach (var v in (ReadOnlySpan<int>)[a, b, c])
            {
                var key = (_parts[v], Key(v));
                welded[key] = welded.GetValueOrDefault(key) + face;
            }
        }
        for (var v = 0; v < positions.Length; v++)
        {
            var n = welded.GetValueOrDefault((_parts[v], Key(v)));
            normals[v] = n == Vector3.Zero ? Vector3.UnitX : Vector3.Normalize(n);
        }

        return new MeshData
        {
            Positions = positions.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray(),
            Normals = normals.SelectMany(n => new[] { n.X, n.Y, n.Z }).ToArray(),
            TexCoords = _uvs.SelectMany(uv => new[] { uv.X, uv.Y }).ToArray(),
            Indices = _indices.ToArray(),
        };
    }
}
