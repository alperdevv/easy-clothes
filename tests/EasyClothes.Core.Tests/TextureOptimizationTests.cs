using EasyClothes.Core.Analysis;
using EasyClothes.Core.Model;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Validation;

namespace EasyClothes.Core.Tests;

public sealed class TextureOptimizationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("easyclothes-tex-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(8192, 0, 0x00020000u)]
    [InlineData(4 * 1024 * 1024, 0xD, 0xD0020009u)]
    [InlineData(1, 0, 0x00020000u)]
    public void Page_flags_match_the_games_single_page_layout(int size, int versionNibble, uint expected) =>
        Assert.Equal(expected, ResourceHeader.FlagsForSize(size, versionNibble));

    [Theory]
    [InlineData(2048, 1024, 1024, 1024, 512)]
    [InlineData(1000, 600, 2048, 512, 512)]
    [InlineData(4096, 4096, 2048, 2048, 2048)]
    [InlineData(8, 2, 2048, 8, 4)]
    public void Target_size_fits_and_rounds_down_to_powers_of_two(int w, int h, int max, int expectedW, int expectedH) =>
        Assert.Equal((expectedW, expectedH), TextureOptimizer.TargetSize(w, h, max));

    [Fact]
    public void Box_filter_averages_covered_pixels()
    {
        byte[] pixels = [0, 0, 0, 255, 200, 100, 50, 255, 0, 0, 0, 255, 200, 100, 50, 255];

        var result = ImageResampler.Resize(new RgbaImage(4, 1, pixels), 2, 1);

        Assert.Equal([100, 50, 25, 255, 100, 50, 25, 255], result.Pixels);
    }

    [Fact]
    public void Uncompressed_opaque_texture_is_resized_and_compressed_to_dxt1()
    {
        var path = Path.Combine(_dir, "a.ytd");
        TestResources.WriteTextureDictionary(path, "shirt_diff_000_a", 256, 128, TextureFormat.A8R8G8B8, TestResources.SolidArgb(256, 128, 200, 30, 30, 255));

        var result = TextureOptimizer.Optimize(path, 64);

        var resource = ResourceFile.Read(path);
        var info = Assert.Single(TextureDictionary.Read(resource));
        Assert.Equal(("shirt_diff_000_a", 64, 32, "DXT1", 4), (info.Name, info.Width, info.Height, info.Format, info.MipLevels));
        var pixel = TextureDictionary.Decode(resource, info).Pixels[..4];
        Assert.InRange(pixel[0], 190, 210);
        Assert.InRange(pixel[1], 20, 40);
        Assert.True(result.NewGraphicsSize < result.OldGraphicsSize);
    }

    [Fact]
    public void Transparent_texture_becomes_dxt5()
    {
        var path = Path.Combine(_dir, "b.ytd");
        TestResources.WriteTextureDictionary(path, "decal", 64, 64, TextureFormat.A8R8G8B8, TestResources.SolidArgb(64, 64, 10, 20, 30, 128));

        TextureOptimizer.Optimize(path, 2048);

        var info = Assert.Single(TextureDictionary.Read(ResourceFile.Read(path)));
        Assert.Equal(TextureFormat.Dxt5, info.PixelFormat);
    }

    [Fact]
    public void Validator_flags_oversized_textures_without_mips_and_the_fix_clears_them()
    {
        var project = new ClothingProject
        {
            Name = "t",
            CollectionName = "t",
            Drawables = [new Drawable { Name = "hat", Prop = PropSlot.Hat, ModelPath = "hat.ydd", Textures = [new() { TexturePath = "hat.ytd" }] }],
        };
        File.WriteAllBytes(Path.Combine(_dir, "hat.ydd"), []);
        TestResources.WriteTextureDictionary(Path.Combine(_dir, "hat.ytd"), "hat", 4096, 4096, TextureFormat.Dxt1, new byte[4096 * 4096 / 2]);
        var cache = new AnalysisCache();
        var limits = new AnalysisLimits();

        var textureIssues = ProjectValidator.Validate(project, _dir, cache, limits).Where(i => i.Fix is not null).ToList();

        Assert.Equal(2, textureIssues.Count);
        IssueFixer.Apply(textureIssues.First(i => i.Fix is ResizeTextureFix).Fix!, _dir);
        Assert.DoesNotContain(ProjectValidator.Validate(project, _dir, cache, limits), i => i.Fix is not null);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_dir, EasyClothes.Core.Projects.ProjectFile.BackupsFolder), "hat.ytd", SearchOption.AllDirectories));
    }

    [Fact]
    public void Uncompressed_texture_gets_a_compress_fix_that_keeps_its_size()
    {
        var project = SingleHat();
        TestResources.WriteTextureDictionary(Path.Combine(_dir, "hat.ytd"), "hat", 512, 512, TextureFormat.A8R8G8B8, TestResources.SolidArgb(512, 512, 40, 90, 160, 255), levels: 1);
        var cache = new AnalysisCache();

        var fixes = IssueFixer.OnePerFile(ProjectValidator.Validate(project, _dir, cache).Select(i => i.Fix).OfType<FixAction>());

        Assert.Equal(new CompressTextureFix("hat.ytd", 512), Assert.Single(fixes));
        IssueFixer.Apply(fixes[0], _dir);
        var info = Assert.Single(TextureDictionary.Read(ResourceFile.Read(Path.Combine(_dir, "hat.ytd"))));
        Assert.Equal((512, 512, TextureFormat.Dxt1), (info.Width, info.Height, info.PixelFormat));
        Assert.DoesNotContain(ProjectValidator.Validate(project, _dir, new AnalysisCache()), i => i.Fix is not null);
    }

    [Fact]
    public void Texture_over_the_fivem_limit_has_a_fix_that_brings_it_under()
    {
        var project = SingleHat();
        var path = Path.Combine(_dir, "hat.ytd");
        TestResources.WriteTextureDictionary(path, "hat", 2048, 4096, TextureFormat.A8R8G8B8, new byte[2048 * 4096 * 4]);

        var oversized = Assert.Single(ProjectValidator.Validate(project, _dir, new AnalysisCache()), i => i.Message.Contains("MiB en memoria"));

        Assert.NotNull(oversized.Fix);
        IssueFixer.Apply(oversized.Fix, _dir);
        Assert.InRange(ResourceFile.Read(path).Header.GraphicsSize, 1, AnalysisLimits.FiveMOversizedBytes);
    }

    [Theory]
    [InlineData(8192, 1, 8192, 2048)]
    [InlineData(2048, 3, 2048, 1024)]
    [InlineData(1024, 1, 2048, 1024)]
    public void Side_that_fits_halves_until_the_dxt_textures_fit(int side, int count, int maxSide, int expected)
    {
        var textures = Enumerable.Range(0, count).Select(i => new TextureInfo($"t{i}", side, side, "A8R8G8B8", 1)).ToList();

        Assert.Equal(expected, ProjectValidator.SideThatFits(textures, maxSide));
    }

    [Fact]
    public void Folder_optimizer_fixes_every_texture_and_keeps_the_originals_outside()
    {
        var resource = Directory.CreateDirectory(Path.Combine(_dir, "res")).FullName;
        Directory.CreateDirectory(Path.Combine(resource, "stream"));
        Directory.CreateDirectory(Path.Combine(resource, ".backups"));
        var heavy = Path.Combine(resource, "stream", "a.ytd");
        TestResources.WriteTextureDictionary(heavy, "a", 512, 512, TextureFormat.A8R8G8B8, TestResources.SolidArgb(512, 512, 200, 30, 30, 255));
        TestResources.WriteTextureDictionary(Path.Combine(resource, "stream", "b.ytd"), "b", 64, 64, TextureFormat.Dxt1, new byte[64 * 64 / 2]);
        TestResources.WriteTextureDictionary(Path.Combine(resource, ".backups", "c.ytd"), "c", 512, 512, TextureFormat.A8R8G8B8, new byte[512 * 512 * 4]);
        var original = File.ReadAllBytes(heavy);
        var backup = Path.Combine(_dir, "backup");

        var planned = FolderOptimizer.Optimize(resource, backup, dryRun: true);

        Assert.Equal((2, Path.Combine("stream", "a.ytd")), (planned.Scanned, Assert.Single(planned.Changes).File));
        Assert.Equal(original, File.ReadAllBytes(heavy));
        Assert.Null(planned.BackupDir);

        var applied = FolderOptimizer.Optimize(resource, backup, dryRun: false);

        Assert.True(Assert.Single(applied.Changes).NewGraphicsSize < applied.Changes[0].OldGraphicsSize);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(backup, "stream", "a.ytd")));
        Assert.Empty(FolderOptimizer.Optimize(resource, backup, dryRun: true).Changes);
        Assert.Throws<ArgumentException>(() => FolderOptimizer.Optimize(resource, Path.Combine(resource, "bk"), dryRun: false));
    }

    private ClothingProject SingleHat()
    {
        File.WriteAllBytes(Path.Combine(_dir, "hat.ydd"), []);
        return new ClothingProject
        {
            Name = "t",
            CollectionName = "t",
            Drawables = [new Drawable { Name = "hat", Prop = PropSlot.Hat, ModelPath = "hat.ydd", Textures = [new() { TexturePath = "hat.ytd" }] }],
        };
    }
}
