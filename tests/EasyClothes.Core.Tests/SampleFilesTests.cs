using EasyClothes.Core.Rage;

namespace EasyClothes.Core.Tests;

/// <summary>
/// Runs the readers over real game-format files. The files are not part of the repo:
/// point EASYCLOTHES_SAMPLES at a folder of .ydd/.ytd files to enable these tests.
/// </summary>
public class SampleFilesTests
{
    private static readonly string? SamplesDir = Environment.GetEnvironmentVariable("EASYCLOTHES_SAMPLES");

    [Fact]
    public void Every_sample_texture_dictionary_lists_its_textures()
    {
        var failures = new List<string>();
        foreach (var path in Samples("*.ytd"))
        {
            try
            {
                var resource = ResourceFile.Read(path);
                var textures = TextureDictionary.Read(resource);
                if (textures.Count == 0) failures.Add($"{path}: no textures");
                foreach (var t in textures)
                {
                    if (t.Name.Length == 0 || t.Width == 0 || t.Height == 0 || t.MipLevels == 0)
                        failures.Add($"{path}: suspicious texture {t}");
                    var image = TextureDictionary.Decode(resource, t);
                    if (image.Pixels.Length != t.Width * t.Height * 4)
                        failures.Add($"{path}: {t.Name} decoded to the wrong size");
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                failures.Add($"{path}: {ex.Message}");
            }
        }
        Assert.Empty(failures);
    }

    [Fact]
    public void Every_sample_drawable_dictionary_has_consistent_geometry()
    {
        var failures = new List<string>();
        foreach (var path in Samples("*.ydd"))
        {
            try
            {
                var drawables = DrawableDictionary.Read(ResourceFile.Read(path), includeMeshes: true);
                if (drawables.Count == 0) failures.Add($"{path}: no drawables");
                foreach (var d in drawables)
                {
                    if (d.Lods.Count == 0 || d.Lods[0].Level != LodLevel.High)
                        failures.Add($"{path}: {d.Name} has no high LOD");
                    foreach (var g in d.Lods.SelectMany(l => l.Models).SelectMany(m => m))
                    {
                        if (g.Indices != g.Triangles * 3 || g.Vertices == 0 || g.VertexStride == 0
                            || g.Mesh!.VertexCount != g.Vertices || g.Mesh.Indices.Length != g.Indices
                            || g.Mesh.Positions.Any(p => !float.IsFinite(p) || Math.Abs(p) > 10))
                            failures.Add($"{path}: {d.Name} geometry {g}");
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                failures.Add($"{path}: {ex.Message}");
            }
        }
        Assert.Empty(failures);
    }

    private static IEnumerable<string> Samples(string pattern) =>
        SamplesDir is null ? [] : Directory.EnumerateFiles(SamplesDir, pattern, SearchOption.AllDirectories);
}
