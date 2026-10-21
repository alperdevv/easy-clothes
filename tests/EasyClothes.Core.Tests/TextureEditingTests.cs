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
    public void Untouched_document_reproduces_the_original()
    {
        var original = Solid(8, 8, 10, 120, 200, 255);
        Assert.Equal(original.Pixels, TextureCompositor.Compose(TextureDocument.FromTexture(original)).Pixels);
    }

    [Fact]
    public void Adjustment_layers_recolour_what_is_below_and_keep_alpha()
    {
        var document = TextureDocument.FromTexture(Solid(4, 4, 255, 0, 0, 128));
        document.Layers.Add(new AdjustmentLayer { Name = "hue", Hue = 120 });
        var shifted = Pixel(TextureCompositor.Compose(document), 0, 0);
        Assert.True(shifted[1] > 200 && shifted[0] < 60, $"{shifted[0]},{shifted[1]},{shifted[2]}");
        Assert.Equal(128, shifted[3]);

        document.Layers[^1] = new AdjustmentLayer { Name = "grey", Saturation = -1, Opacity = 0.5f };
        var half = Pixel(TextureCompositor.Compose(document), 0, 0);
        Assert.InRange(half[0], 140, 220);
        Assert.InRange(half[1], 30, 60);
    }

    [Fact]
    public void Image_layers_land_where_placed_and_the_cut_out_survives()
    {
        var document = TextureDocument.FromTexture(Solid(64, 64, 0, 0, 0, 200));
        document.Layers.Add(new ImageLayer { Name = "logo", Image = Solid(4, 4, 255, 255, 255, 255), X = 0.25f, Y = 0.75f, Scale = 0.25f });

        var result = TextureCompositor.Compose(document);

        Assert.True(document.KeepOriginalAlpha);
        Assert.Equal([255, 255, 255, 200], Pixel(result, 16, 48));
        Assert.Equal([0, 0, 0, 200], Pixel(result, 48, 16));
    }

    [Theory]
    [InlineData(BlendMode.Normal, 128, 128, 128)]
    [InlineData(BlendMode.Multiply, 128, 0, 0)]
    [InlineData(BlendMode.Screen, 255, 128, 128)]
    [InlineData(BlendMode.Darken, 128, 0, 0)]
    [InlineData(BlendMode.Lighten, 255, 128, 128)]
    [InlineData(BlendMode.Difference, 127, 128, 128)]
    public void Blend_modes_mix_with_the_layers_below(BlendMode mode, byte r, byte g, byte b)
    {
        var document = TextureDocument.FromTexture(Solid(2, 2, 255, 0, 0, 255));
        document.Layers.Add(new FillLayer { Name = "grey", Rgb = 0x808080, Blend = mode });
        Assert.Equal([r, g, b, 255], Pixel(TextureCompositor.Compose(document), 0, 0));
    }

    [Fact]
    public void Colour_blend_recolours_but_keeps_the_shading()
    {
        var document = new TextureDocument(2, 1);
        document.Layers.Add(new PixelLayer { Name = "shade", Image = new RgbaImage(2, 1, [40, 40, 40, 255, 200, 200, 200, 255]) });
        document.Layers.Add(new FillLayer { Name = "blue", Rgb = 0x0000FF, Blend = BlendMode.Color });

        var result = TextureCompositor.Compose(document);

        Assert.True(Pixel(result, 0, 0)[2] > Pixel(result, 0, 0)[0]);
        Assert.True(Pixel(result, 1, 0)[0] > Pixel(result, 0, 0)[0]);
    }

    [Fact]
    public void Brush_paints_and_erases_a_pixel_layer()
    {
        var layer = TextureDocument.Blank(32, 32);
        var brush = new BrushSettings(0x00FF00, Radius: 4, Hardness: 1, Opacity: 1, Erase: false);

        Brush.Stroke(layer, new Vector2(4, 16), new Vector2(28, 16), brush);
        Assert.Equal([0, 255, 0, 255], Pixel(layer, 16, 16));

        Brush.Stroke(layer, new Vector2(16, 16), new Vector2(16, 16), brush with { Erase = true });
        Assert.Equal(0, Pixel(layer, 16, 16)[3]);
        Assert.Equal([0, 255, 0, 255], Pixel(layer, 6, 16));
    }

    [Fact]
    public void History_undoes_redoes_and_collapses_slider_drags()
    {
        var document = TextureDocument.FromTexture(Solid(4, 4, 255, 0, 0, 255));
        var history = new EditHistory(document, "Abrir");
        var layer = document.Layers[^1];

        layer.Opacity = 0.8f;
        history.Commit(document, "Opacidad", "opacity");
        layer.Opacity = 0.5f;
        history.Commit(document, "Opacidad", "opacity");
        document.Layers.Add(new FillLayer { Name = "fill" });
        history.Commit(document, "Nueva capa");

        Assert.Equal(["Abrir", "Opacidad", "Nueva capa"], history.Entries.Select(e => e.Label));
        document.Restore(history.Undo());
        Assert.Equal(2, document.Layers.Count);
        Assert.Equal(0.5f, document.Layers[^1].Opacity);
        document.Restore(history.Undo());
        Assert.Equal(1f, document.Layers[^1].Opacity);
        Assert.False(history.IsModified);

        document.Restore(history.Redo());
        Assert.Equal(0.5f, document.Layers[^1].Opacity);
        document.Layers[^1].Name = "renamed";
        history.Commit(document, "Renombrar");
        Assert.False(history.CanRedo);
        Assert.Equal(1f, history.Entries[0].State.Layers[^1].Opacity);
    }

    [Fact]
    public void History_drops_the_oldest_states_past_its_budget()
    {
        var document = new TextureDocument(16, 16);
        document.Layers.Add(document.NewPixelLayer("paint"));
        var history = new EditHistory(document, "Abrir", budgetBytes: 16 * 16 * 4 * 3);
        for (var i = 0; i < 6; i++)
        {
            ((PixelLayer)document.Layers[0]).Image = TextureDocument.Blank(16, 16);
            history.Commit(document, $"stroke {i}");
        }

        Assert.Equal(3, history.Entries.Count);
        Assert.Equal("stroke 5", history.Entries[history.Index].Label);
    }

    [Fact]
    public void Rasterize_and_merge_down_bake_the_same_result()
    {
        var document = TextureDocument.FromTexture(Solid(8, 8, 255, 0, 0, 255));
        document.Layers.RemoveAt(1);
        document.Layers.Add(new FillLayer { Name = "multiply", Rgb = 0x808080, Blend = BlendMode.Multiply });
        document.Layers.Add(new ImageLayer { Name = "logo", Image = Solid(2, 2, 0, 0, 255, 255), Scale = 0.5f });
        var before = TextureCompositor.Compose(document).Pixels;

        var raster = document.Rasterize(document.Layers[^1]);
        Assert.IsType<PixelLayer>(document.Layers[^1]);
        Assert.Equal("logo", raster.Name);
        Assert.Equal(before, TextureCompositor.Compose(document).Pixels);

        document.MergeDown(document.Layers[1]);
        Assert.Equal(before, TextureCompositor.Compose(document).Pixels);
        document.MergeDown(document.Layers[1]);
        Assert.Single(document.Layers);
        Assert.Equal(before, TextureCompositor.Compose(document).Pixels);
    }

    [Fact]
    public void Layers_round_trip_through_the_layered_file()
    {
        var document = TextureDocument.FromTexture(Solid(8, 8, 255, 0, 0, 200));
        document.Layers.Add(new ImageLayer { Name = "logo", Image = Solid(2, 2, 0, 0, 255, 255), X = 0.3f, Rotation = 45 });
        document.Layers.Add(new AdjustmentLayer { Name = "adjust", Hue = 30, Blend = BlendMode.Normal, Opacity = 0.7f, Visible = false });
        document.Layers.Add(new FillLayer { Name = "fill", Rgb = 0x123456, Blend = BlendMode.Overlay, Locked = true });
        var path = Path.Combine(_dir, "t" + TextureDocumentFile.Extension);

        TextureDocumentFile.Save(document, path, "abc");
        var loaded = TextureDocumentFile.TryLoad(path, "ABC")!;

        Assert.Null(TextureDocumentFile.TryLoad(path, "other"));
        Assert.Equal(document.Layers.Select(l => (l.Id, l.Name, l.GetType(), l.Visible, l.Locked, l.Opacity, l.Blend)),
            loaded.Layers.Select(l => (l.Id, l.Name, l.GetType(), l.Visible, l.Locked, l.Opacity, l.Blend)));
        Assert.Equal(45, ((ImageLayer)loaded.Layers[2]).Rotation);
        Assert.Equal(0x123456u, ((FillLayer)loaded.Layers[4]).Rgb);
        Assert.True(loaded.KeepOriginalAlpha);
        Assert.Equal(TextureCompositor.Compose(document).Pixels, TextureCompositor.Compose(loaded).Pixels);
    }

    [Fact]
    public void Saving_adds_a_variation_or_overwrites_with_a_backup_and_keeps_the_layers()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "assets"));
        using (var file = File.Create(Path.Combine(_dir, "assets", "jbib_diff_000_a_uni.ytd")))
            TextureDictionaryWriter.Create([("jbib_diff_000_a_uni", TextureEncoder.Encode(Solid(16, 16, 255, 0, 0, 255), TextureFormat.Dxt1))]).Write(file);
        var drawable = new Drawable { Name = "jbib_000", Component = ComponentSlot.Top, ModelPath = "m.ydd", Textures = [new() { TexturePath = "assets/jbib_diff_000_a_uni.ytd" }] };
        var document = TextureDocument.FromTexture(ImageLoader.Load(Path.Combine(_dir, drawable.Textures[0].TexturePath)));
        document.Layers.Add(new AdjustmentLayer { Name = "grey", Saturation = -1 });

        var variation = TextureEditSaver.Save(_dir, drawable, 0, document, TextureSaveMode.NewVariation);
        Assert.Equal((1, "assets/jbib_diff_000_a_uni_edit2.ytd"), (variation.Index, variation.TexturePath));
        var saved = ImageLoader.Load(Path.Combine(_dir, variation.TexturePath));
        Assert.InRange(saved.Pixels[0] - saved.Pixels[1], -8, 8);
        Assert.Equal(255, ImageLoader.Load(Path.Combine(_dir, drawable.Textures[0].TexturePath)).Pixels[0]);
        var savedPath = Path.Combine(_dir, variation.TexturePath);
        Assert.Equal(3, TextureDocumentFile.TryLoad(TextureDocumentFile.PathFor(savedPath), TextureDocumentFile.HashOf(savedPath))!.Layers.Count);

        var overwrite = TextureEditSaver.Save(_dir, drawable, 0, document, TextureSaveMode.Overwrite);
        Assert.Equal(2, drawable.Textures.Count);
        Assert.True(File.Exists(Path.Combine(_dir, overwrite.BackupPath!)));
        Assert.Equal(255, ImageLoader.Load(Path.Combine(_dir, overwrite.BackupPath!)).Pixels[0]);
        Assert.NotEqual(255, ImageLoader.Load(Path.Combine(_dir, overwrite.TexturePath)).Pixels[0]);
        Assert.True(File.Exists(Path.Combine(_dir, "assets", "jbib_diff_000_a_uni" + TextureDocumentFile.Extension)));
    }

    private static RgbaImage Solid(int w, int h, byte r, byte g, byte b, byte a) =>
        new(w, h, Enumerable.Range(0, w * h).SelectMany(_ => new[] { r, g, b, a }).ToArray());

    private static byte[] Pixel(RgbaImage image, int x, int y) => image.Pixels[((y * image.Width + x) * 4)..((y * image.Width + x) * 4 + 4)];
}
