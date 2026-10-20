using EasyClothes.Core.Model;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Rage.Meta;

namespace EasyClothes.Core.Tests;

// Set EASYCLOTHES_SAMPLES to a folder of real .ydd/.ytd files to enable these tests.
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

    [Fact]
    public void Sample_textures_survive_optimization()
    {
        var dir = Directory.CreateTempSubdirectory("easyclothes-samples-").FullName;
        try
        {
            foreach (var source in Samples("*.ytd").Take(25))
            {
                var copy = Path.Combine(dir, Path.GetFileName(source));
                File.Copy(source, copy);
                Textures.TextureOptimizer.Optimize(copy, 1024);

                var resource = ResourceFile.Read(copy);
                foreach (var t in TextureDictionary.Read(resource))
                {
                    Assert.True(Math.Max(t.Width, t.Height) <= 1024, $"{source}: {t}");
                    Assert.True(t.MipLevels > 1, $"{source}: {t}");
                    Assert.Equal(t.Width * t.Height * 4, TextureDictionary.Decode(resource, t).Pixels.Length);
                }
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Sample_meshes_survive_reduction()
    {
        var dir = Directory.CreateTempSubdirectory("easyclothes-samples-").FullName;
        try
        {
            foreach (var source in Samples("*.ydd").Where((_, i) => i % 10 == 0))
            {
                var copy = Path.Combine(dir, Path.GetFileName(source));
                File.Copy(source, copy);
                var before = DrawableDictionary.Read(ResourceFile.Read(copy));

                var result = Meshes.MeshReducer.Reduce(copy, before.Sum(d => d.Lods[0].Triangles) / 2);

                var after = DrawableDictionary.Read(ResourceFile.Read(copy), includeMeshes: true);
                Assert.True(result.NewTriangles <= result.OldTriangles, source);
                Assert.Equal(result.NewTriangles, after.Sum(d => d.Lods[0].Triangles));
                foreach (var g in after.SelectMany(d => d.Lods).SelectMany(l => l.Models).SelectMany(m => m))
                    Assert.True(g.Indices == g.Triangles * 3 && g.Mesh!.VertexCount == g.Vertices, source);
                Assert.Equal(before.Select(d => d.Lods.Skip(1).Sum(l => l.Triangles)), after.Select(d => d.Lods.Skip(1).Sum(l => l.Triangles)));
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Sample_variation_ymts_match_the_streamed_files_and_rewrite_identically()
    {
        foreach (var path in Samples("*_freemode_01_*.ymt"))
        {
            var original = ResourceFile.Read(path);
            var variation = PedVariation.Read(original);
            var stream = Path.GetDirectoryName(path)!;
            var collection = Path.GetFileNameWithoutExtension(path);
            var dlc = collection["mp_m_freemode_01_".Length..];

            foreach (var (slot, counts) in variation.Components)
            {
                var code = PedSlotNames.FileCode(slot);
                Assert.Equal(Directory.GetFiles(stream, $"{collection}^{code}_???_u.ydd").Length, counts.Count);
                for (var d = 0; d < counts.Count; d++)
                    Assert.Equal(Directory.GetFiles(stream, $"{collection}^{code}_diff_{d:000}_?_uni.ytd").Length, counts[d]);
            }
            var propCollection = collection.Replace("_freemode_01_", "_freemode_01_p_");
            foreach (var (slot, counts) in variation.Props)
                Assert.Equal(Directory.GetFiles(stream, $"{propCollection}^{PedSlotNames.FileCode(slot)}_???.ydd").Length, counts.Count);

            var rewritten = new MetaReader(PedVariation.Write(dlc, variation.Components, variation.Props));
            var source = new MetaReader(original);
            Assert.Equal(source.Blocks.Select(b => b.Type), rewritten.Blocks.Select(b => b.Type));
            for (var i = 0; i < source.Blocks.Count; i++)
                Assert.True(source.Block(i).TrimEnd((byte)0).SequenceEqual(rewritten.Block(i).TrimEnd((byte)0)), $"{path}: block {i} differs");
        }
    }

    private static IEnumerable<string> Samples(string pattern) =>
        SamplesDir is null ? [] : Directory.EnumerateFiles(SamplesDir, pattern, SearchOption.AllDirectories);
}
