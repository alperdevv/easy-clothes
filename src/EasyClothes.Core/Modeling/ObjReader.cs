using System.Globalization;
using System.Numerics;
using EasyClothes.Core.Rage;

namespace EasyClothes.Core.Modeling;

public sealed record ObjModel(MeshData Mesh, IReadOnlyList<string> Warnings, string? TexturePath);

// Wavefront .obj as Blender and most tools export it: polygons are fanned into triangles, missing
// normals are smoothed from the faces and V is flipped to the game's top-left texture origin.
public static class ObjReader
{
    public static ObjModel Read(string path)
    {
        var positions = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normals = new List<Vector3>();
        var corners = new Dictionary<(int, int, int), ushort>();
        var outPositions = new List<Vector3>();
        var outUvs = new List<Vector2>();
        var outNormals = new List<Vector3?>();
        var indices = new List<ushort>();
        var warnings = new List<string>();
        string? material = null;
        var lineNumber = 0;

        foreach (var raw in File.ReadLines(path))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "v":
                    positions.Add(new Vector3(Number(parts, 1, lineNumber), Number(parts, 2, lineNumber), Number(parts, 3, lineNumber)));
                    break;
                case "vt":
                    uvs.Add(new Vector2(Number(parts, 1, lineNumber), 1 - (parts.Length > 2 ? Number(parts, 2, lineNumber) : 0)));
                    break;
                case "vn":
                    normals.Add(Vector3.Normalize(new Vector3(Number(parts, 1, lineNumber), Number(parts, 2, lineNumber), Number(parts, 3, lineNumber))));
                    break;
                case "mtllib":
                    material ??= Path.Combine(Path.GetDirectoryName(path) ?? "", line[6..].Trim());
                    break;
                case "f":
                    if (parts.Length < 4) throw new InvalidDataException($"Línea {lineNumber}: una cara necesita al menos 3 vértices.");
                    var face = parts.Skip(1).Select(p => Corner(p, positions.Count, uvs.Count, normals.Count, lineNumber)).ToList();
                    var ids = face.Select(c =>
                    {
                        if (corners.TryGetValue(c, out var id)) return id;
                        if (outPositions.Count == ushort.MaxValue)
                            throw new InvalidDataException("El modelo tiene más de 65.535 vértices; simplifícalo en Blender (Decimate) antes de importarlo.");
                        id = (ushort)outPositions.Count;
                        outPositions.Add(positions[c.Item1]);
                        outUvs.Add(c.Item2 >= 0 ? uvs[c.Item2] : Vector2.Zero);
                        outNormals.Add(c.Item3 >= 0 ? normals[c.Item3] : null);
                        corners[c] = id;
                        return id;
                    }).ToList();
                    for (var k = 1; k + 1 < ids.Count; k++)
                    {
                        indices.Add(ids[0]);
                        indices.Add(ids[k]);
                        indices.Add(ids[k + 1]);
                    }
                    break;
            }
        }

        if (indices.Count == 0) throw new InvalidDataException("El .obj no tiene caras.");
        if (uvs.Count == 0) warnings.Add("El modelo no tiene coordenadas UV: la textura no se verá bien. Desenvuélvelo (Unwrap) en Blender.");
        var smooth = SmoothNormals(outPositions, indices);
        var mesh = new MeshData
        {
            Positions = outPositions.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray(),
            Normals = outNormals.Select((n, i) => n ?? smooth[i]).SelectMany(n => new[] { n.X, n.Y, n.Z }).ToArray(),
            TexCoords = outUvs.SelectMany(t => new[] { t.X, t.Y }).ToArray(),
            Indices = indices.ToArray(),
        };
        return new ObjModel(mesh, warnings, material is null ? null : DiffuseFrom(material, warnings));
    }

    // v, v/vt, v//vn or v/vt/vn; indices start at 1 and negatives count back from the end.
    private static (int, int, int) Corner(string text, int positions, int uvs, int normals, int line)
    {
        var bits = text.Split('/');
        int Index(int i, int count, bool required)
        {
            if (i >= bits.Length || bits[i].Length == 0)
                return required ? throw new InvalidDataException($"Línea {line}: vértice '{text}' sin posición.") : -1;
            if (!int.TryParse(bits[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value == 0)
                throw new InvalidDataException($"Línea {line}: índice '{bits[i]}' no válido.");
            var index = value > 0 ? value - 1 : count + value;
            return index >= 0 && index < count ? index : throw new InvalidDataException($"Línea {line}: el índice {value} no existe.");
        }
        return (Index(0, positions, true), Index(1, uvs, false), Index(2, normals, false));
    }

    private static float Number(string[] parts, int i, int line) =>
        i < parts.Length && float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidDataException($"Línea {line}: número no válido.");

    private static Vector3[] SmoothNormals(List<Vector3> positions, List<ushort> indices)
    {
        var normals = new Vector3[positions.Count];
        for (var t = 0; t < indices.Count; t += 3)
        {
            var (a, b, c) = (indices[t], indices[t + 1], indices[t + 2]);
            var face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            normals[a] += face;
            normals[b] += face;
            normals[c] += face;
        }
        return normals.Select(n => n == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(n)).ToArray();
    }

    // The first diffuse map named in the .mtl, if the file is there.
    private static string? DiffuseFrom(string material, List<string> warnings)
    {
        if (!File.Exists(material)) return null;
        var map = File.ReadLines(material).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("map_Kd ", StringComparison.Ordinal));
        if (map is null) return null;
        var texture = Path.Combine(Path.GetDirectoryName(material) ?? "", map[7..].Trim());
        if (File.Exists(texture)) return texture;
        warnings.Add($"El material apunta a {Path.GetFileName(texture)}, que no está junto al modelo.");
        return null;
    }
}
