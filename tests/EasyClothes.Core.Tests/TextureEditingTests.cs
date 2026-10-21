using System.Numerics;
using EasyClothes.Core.Model;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Textures.Editing;

namespace EasyClothes.Core.Tests;

public sealed class TextureEditingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("easyclothes-edit-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Untouched_edit_reproduces_the_original()
    {
        var original = Solid(8, 8, 10, 120, 200, 255);
        Assert.Equal(original.Pixels, TextureCompositor.Compose(new TextureEdit { Original = original }).Pixels);
    }

    [Fact]
    public void Adjustments_shift_hue_drop_saturation_and_keep_alpha()
    {
        var red = Solid(4, 4, 255, 0, 0, 128);

        var shifted = Pixel(TextureCompositor.Compose(new TextureEdit { Original = red, Hue = 120 }), 0, 0);
        Assert.True(shifted[1] > 200 && shifted[0] < 60, $"{shifted[0]},{shifted[1]},{shifted[2]}");

        var grey = Pixel(TextureCompositor.Compose(new TextureEdit { Original = red, Saturation = -1 }), 0, 0);
        Assert.Equal(grey[0], grey[1]);
        Assert.Equal(grey[1], grey[2]);
        Assert.Equal(128, grey[3]);
    }

    [Fact]
    public void Decals_land_where_placed_without_touching_the_cut_out()
    {
        var edit = new TextureEdit { Original = Solid(64, 64, 0, 0, 0, 200) };
        edit.Decals.Add(new Decal { Name = "logo", Image = Solid(4, 4, 255, 255, 255, 255), X = 0.25f, Y = 0.75f, Scale = 0.25f });

        var result = TextureCompositor.Compose(edit);

        Assert.Equal([255, 255, 255, 200], Pixel(result, 16, 48));
        Assert.Equal([0, 0, 0, 200], Pixel(result, 48, 16));
    }

    [Fact]
    public void Brush_paints_and_erases_on_its_own_layer()
    {
        var edit = new TextureEdit { Original = Solid(32, 32, 0, 0, 0, 255) };
        var brush = new BrushSettings(0x00FF00, Radius: 4, Hardness: 1, Opacity: 1, Erase: false);

        Brush.Stroke(edit, new Vector2(4, 16), new Vector2(28, 16), brush);
        Assert.Equal([0, 255, 0, 255], Pixel(TextureCompositor.Compose(edit), 16, 16));

        Brush.Stroke(edit, new Vector2(16, 16), new Vector2(16, 16), brush with { Erase = true });
        Assert.Equal([0, 0, 0, 255], Pixel(TextureCompositor.Compose(edit), 16, 16));
        Assert.Equal([0, 255, 0, 255], Pixel(TextureCompositor.Compose(edit), 6, 16));
    }

    [Fact]
    public void Saving_adds_a_variation_or_overwrites_with_a_backup()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "assets"));
        using (var file = File.Create(Path.Combine(_dir, "assets", "jbib_diff_000_a_uni.ytd")))
            TextureDictionaryWriter.Create([("jbib_diff_000_a_uni", TextureEncoder.Encode(Solid(16, 16, 255, 0, 0, 255), TextureFormat.Dxt1))]).Write(file);
        var drawable = new Drawable { Name = "jbib_000", Component = ComponentSlot.Top, ModelPath = "m.ydd", Textures = [new() { TexturePath = "assets/jbib_diff_000_a_uni.ytd" }] };
        var edit = new TextureEdit { Original = ImageLoader.Load(Path.Combine(_dir, drawable.Textures[0].TexturePath)), Saturation = -1 };

        var variation = TextureEditSaver.Save(_dir, drawable, 0, edit, TextureSaveMode.NewVariation);
        Assert.Equal((1, "assets/jbib_diff_000_a_uni_edit2.ytd"), (variation.Index, variation.TexturePath));
        var saved = ImageLoader.Load(Path.Combine(_dir, variation.TexturePath));
        Assert.InRange(saved.Pixels[0] - saved.Pixels[1], -8, 8);
        Assert.Equal(255, ImageLoader.Load(Path.Combine(_dir, drawable.Textures[0].TexturePath)).Pixels[0]);

        var overwrite = TextureEditSaver.Save(_dir, drawable, 0, edit, TextureSaveMode.Overwrite);
        Assert.Equal(2, drawable.Textures.Count);
        Assert.True(File.Exists(Path.Combine(_dir, overwrite.BackupPath!)));
        Assert.Equal(255, ImageLoader.Load(Path.Combine(_dir, overwrite.BackupPath!)).Pixels[0]);
        Assert.NotEqual(255, ImageLoader.Load(Path.Combine(_dir, overwrite.TexturePath)).Pixels[0]);
    }

    private static RgbaImage Solid(int w, int h, byte r, byte g, byte b, byte a) =>
        new(w, h, Enumerable.Range(0, w * h).SelectMany(_ => new[] { r, g, b, a }).ToArray());

    private static byte[] Pixel(RgbaImage image, int x, int y) => image.Pixels[((y * image.Width + x) * 4)..((y * image.Width + x) * 4 + 4)];
}
