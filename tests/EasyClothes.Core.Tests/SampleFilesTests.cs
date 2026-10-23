using EasyClothes.Core.Importing;
using EasyClothes.Core.Analysis;
using EasyClothes.Core.Model;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Rage.Meta;
using EasyClothes.Core.Viewing;

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
                        if (g.Mesh?.BlendWeights is { } weights && weights.Chunk(4).Any(w => Math.Abs(w.Sum(x => x) - 255) > 3))
                            failures.Add($"{path}: {d.Name} has bone weights that don't add up");
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
                    Assert.Equal(Directory.GetFiles(stream, $"{collection}^{code}_diff_{d:000}_?_uni.ytd").Length, counts[d].Textures);
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

    [Fact]
    public void Sample_resource_imports_every_garment_at_its_number()
    {
        if (SamplesDir is null) return;
        var dir = Directory.CreateTempSubdirectory("easyclothes-eup-").FullName;
        try
        {
            var project = new ClothingProject { Name = "EUP", CollectionName = "eup" };
            var result = ResourceImporter.Import(project, dir, SamplesDir);

            Assert.Equal("eup", result.Collection);
            Assert.DoesNotContain(result.Warnings, w => w.Contains("no aparecen"));
            foreach (var path in Samples("*_freemode_01_*.ymt"))
            {
                var variation = PedVariation.Read(ResourceFile.Read(path));
                var gender = Path.GetFileName(path).StartsWith("mp_m") ? PedGender.Male : PedGender.Female;
                foreach (var (slot, items) in variation.Components)
                {
                    var garments = ClothingProject.InGameOrder(project.Drawables.Where(d => d.Gender == gender && d.Component == slot)).ToList();
                    Assert.Equal(items.Select(i => i.Textures), garments.Select(g => g.Reserved ? 1 : g.Textures.Count));
                    Assert.All(garments.Where(g => !g.Reserved).Select((g, i) => (g, i)), x => Assert.Contains($"_{x.i:000}_", Path.GetFileName(x.g.ModelPath)));
                }
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Sample_creature_metadata_reads_and_rewrites_byte_for_byte()
    {
        foreach (var path in Samples("mp_creaturemetadata_*.ymt"))
        {
            var original = File.ReadAllBytes(path);
            var document = Rbf.Read(original);
            Assert.Equal("CCreatureMetaData", document.Name.LocalName);
            Assert.Equal(original, Rbf.Write(document));
        }
    }

    // Set EASYCLOTHES_BODY to the exported freemode ped folder (with mp_f_freemode_01/feet_*.ydd).
    [Fact]
    public void Heel_height_is_estimated_from_the_shoe_sole()
    {
        if (Environment.GetEnvironmentVariable("EASYCLOTHES_BODY") is not { } body) return;
        var female = Path.Combine(body, "mp_f_freemode_01");

        Assert.Equal(1.0f, HeelEstimator.Estimate(Path.Combine(female, "feet_000_r.ydd")));
        Assert.Null(HeelEstimator.Estimate(Path.Combine(female, "feet_001_u.ydd")));
    }

    [Fact]
    public void Ped_skeleton_places_the_head_above_the_shoulders()
    {
        if (Environment.GetEnvironmentVariable("EASYCLOTHES_BODY") is not { } body) return;

        foreach (var gender in Enum.GetValues<PedGender>())
        {
            var bones = Skeleton.ReadFragment(ResourceFile.Read(Path.Combine(body, PedSlotNames.PedModel(gender) + ".yft")));
            var head = Assert.Single(bones, b => b.Tag == Skeleton.HeadTag);
            Assert.Equal("SKEL_Head", head.Name);
            Assert.InRange(head.World.Translation.Z, 0.55f, 0.7f);
            Assert.NotNull(BaseBody.Fit(body, gender, null, PropSlot.Watch).Anchor);
        }
    }

    private static IEnumerable<string> Samples(string pattern) =>
        SamplesDir is null ? [] : Directory.EnumerateFiles(SamplesDir, pattern, SearchOption.AllDirectories);
}
