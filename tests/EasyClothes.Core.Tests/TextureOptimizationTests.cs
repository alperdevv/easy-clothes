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
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_dir, IssueFixer.BackupsFolder), "hat.ytd", SearchOption.AllDirectories));
    }
}
