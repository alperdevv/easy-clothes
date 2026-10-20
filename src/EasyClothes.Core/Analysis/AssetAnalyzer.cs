using EasyClothes.Core.Rage;

namespace EasyClothes.Core.Analysis;

public static class AssetAnalyzer
{
    private const float UvTolerance = 0.01f;

    public static ModelReport AnalyzeModel(string path)
    {
        var resource = ResourceFile.Read(path);
        var drawables = DrawableDictionary.Read(resource, includeMeshes: true);
        var lods = drawables.SelectMany(d => d.Lods)
            .GroupBy(l => l.Level)
            .Select(g => new LodReport(g.Key, g.Sum(l => l.Vertices), g.Sum(l => l.Triangles)))
            .OrderBy(l => l.Level)
            .ToList();

        int outOfRange = 0, invalid = 0, degenerate = 0, vertices = 0;
        var highMeshes = drawables.SelectMany(d => d.Lods.Where(l => l.Level == LodLevel.High))
            .SelectMany(l => l.Models).SelectMany(m => m).Select(g => g.Mesh!);
        foreach (var mesh in highMeshes)
        {
            vertices += mesh.VertexCount;
            for (var i = 0; i < mesh.TexCoords.Length; i += 2)
            {
                float u = mesh.TexCoords[i], v = mesh.TexCoords[i + 1];
                if (!float.IsFinite(u) || !float.IsFinite(v)) invalid++;
                else if (u < -UvTolerance || u > 1 + UvTolerance || v < -UvTolerance || v > 1 + UvTolerance) outOfRange++;
            }
            for (var t = 0; t < mesh.Indices.Length; t += 3)
            {
                int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
                if (a == b || b == c || a == c) degenerate++;
            }
        }

        var h = resource.Header;
        return new ModelReport(h.SystemSize, h.GraphicsSize, lods, outOfRange, invalid, degenerate, vertices);
    }

    public static TextureReport AnalyzeTexture(string path)
    {
        var resource = ResourceFile.Read(path);
        return new TextureReport(resource.Header.SystemSize, resource.Header.GraphicsSize, TextureDictionary.Read(resource));
    }
}
