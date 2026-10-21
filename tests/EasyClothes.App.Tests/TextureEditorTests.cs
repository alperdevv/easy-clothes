using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using EasyClothes.App.ViewModels;
using EasyClothes.App.Viewer;
using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Textures.Editing;

namespace EasyClothes.App.Tests;

public sealed class TextureEditorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("easyclothes-editor-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [AvaloniaFact]
    public async Task Layers_preview_live_undo_and_come_back_when_reopened()
    {
        var window = await OpenProjectWithTexture();
        var editor = (await window.ViewModel.CreateTextureEditorAsync())!;
        Assert.Equal(["Capa 1", "Textura original"], editor.Layers.Select(l => l.Name));

        editor.NewAdjustmentLayerCommand.Execute(null);
        editor.SelectedLayer!.Saturation = -50;
        editor.SelectedLayer.Saturation = -100;
        await Settle(editor);
        Assert.Equal(editor.Composed.Pixels[0], editor.Composed.Pixels[1]);
        Assert.Equal(["Abrir textura", "Nueva capa de ajuste", "Saturación"], editor.HistoryItems.Select(h => h.Label));

        editor.AddImage(await WritePng("logo.png", Solid(4, 4, 0, 0, 255)));
        await Settle(editor);
        Assert.Equal([0, 0, 255], Pixel(editor.Composed, 32, 32)[..3]);

        editor.SelectedLayer = editor.Layers.Single(l => l.Name == "Capa 1");
        editor.Tool = EditorTool.Brush;
        editor.BrushColor = Colors.Lime;
        Assert.True(editor.BeginStroke());
        editor.StrokeTo(new Vector2(0.1f, 0.1f), new Vector2(0.2f, 0.1f));
        editor.EndStroke();
        await Settle(editor);
        Assert.Equal([150, 150, 150], Pixel(editor.Composed, 10, 6)[..3]);

        editor.UndoCommand.Execute(null);
        await Settle(editor);
        Assert.NotEqual(150, Pixel(editor.Composed, 10, 6)[1]);
        editor.RedoCommand.Execute(null);
        editor.HistoryIndex = 1;
        await Settle(editor);
        Assert.Equal(3, editor.Layers.Count);
        editor.HistoryIndex = editor.HistoryItems.Count - 1;
        Assert.Equal(4, editor.Layers.Count);

        await editor.SaveAsync(TextureSaveMode.NewVariation);
        window.ViewModel.ApplyTextureEdit(editor);
        Assert.Equal(2, window.ViewModel.Selected!.Textures.Count);
        Assert.Equal(1, window.ViewModel.SelectedTexture?.Index);
        Assert.True(window.ViewModel.IsDirty);
        var saved = ImageLoader.Load(Path.Combine(_dir, window.ViewModel.SelectedTexture!.RelativePath));
        Assert.True(Pixel(saved, 32, 32)[2] > 200);

        var reopened = (await window.ViewModel.CreateTextureEditorAsync())!;
        Assert.Equal(["logo", "Ajuste de color 1", "Capa 1", "Textura original"], reopened.Layers.Select(l => l.Name));
        Assert.Equal("Capas recuperadas de la última edición.", reopened.Status);
        Assert.False(reopened.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public async Task External_edits_land_as_a_layer_on_top_and_can_be_undone()
    {
        var window = await OpenProjectWithTexture();
        var editor = (await window.ViewModel.CreateTextureEditorAsync())!;
        var opened = Pixel(editor.Composed, 5, 5);
        var png = await WritePng("fuera.png", Solid(32, 32, 10, 20, 30));

        await editor.ReloadExternalAsync(png);
        await Settle(editor);

        Assert.Equal("Editado fuera", editor.Layers[0].Name);
        Assert.All(editor.Layers.Skip(1), l => Assert.False(l.Visible));
        Assert.Equal("32x32", editor.SizeLabel);
        Assert.Equal([10, 20, 30, 255], Pixel(editor.Composed, 5, 5));
        Assert.True(editor.HasUnsavedChanges);

        editor.UndoCommand.Execute(null);
        await Settle(editor);
        Assert.Equal("64x64", editor.SizeLabel);
        Assert.Equal(opened, Pixel(editor.Composed, 5, 5));
        Assert.False(editor.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public async Task Painting_needs_an_unlocked_pixel_layer()
    {
        var window = await OpenProjectWithTexture();
        var editor = (await window.ViewModel.CreateTextureEditorAsync())!;

        editor.SelectedLayer!.Locked = true;
        Assert.False(editor.BeginStroke());
        Assert.Equal("La capa está bloqueada.", editor.Status);

        editor.NewFillLayerCommand.Execute(null);
        Assert.False(editor.BeginStroke());
        editor.RasterizeLayerCommand.Execute(null);
        Assert.True(editor.SelectedLayer.IsPixels);
        Assert.True(editor.BeginStroke());
    }

    [AvaloniaFact]
    public async Task Canvas_drags_move_layers_and_the_wheel_zooms()
    {
        var window = await OpenProjectWithTexture();
        var editor = (await window.ViewModel.CreateTextureEditorAsync())!;
        editor.AddImage(await WritePng("logo.png", Solid(4, 4, 255, 0, 0)));

        var editorWindow = new TextureEditorWindow(editor) { Width = 1500, Height = 900 };
        editorWindow.Show();
        Dispatcher.UIThread.RunJobs();
        var canvas = editorWindow.FindControl<TextureCanvas>("Canvas")!;
        var centre = canvas.TranslatePoint(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2), editorWindow)!.Value;
        var side = 64 * canvas.Scale;

        editorWindow.MouseDown(centre, MouseButton.Left);
        editorWindow.MouseMove(centre + new Point(side / 4, 0));
        editorWindow.MouseUp(centre + new Point(side / 4, 0), MouseButton.Left);

        var logo = (ImageLayer)editor.SelectedLayer!.Model;
        Assert.InRange(logo.X, 0.7f, 0.8f);
        Assert.InRange(logo.Y, 0.45f, 0.55f);
        Assert.Equal("Mover capa", editor.HistoryItems[^1].Label);

        editor.SelectedLayer = editor.Layers.Single(l => l.Name == "Capa 1");
        editorWindow.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.None);
        Assert.Equal(EditorTool.Brush, editor.Tool);
        editorWindow.MouseDown(centre - new Point(side / 4, side / 4), MouseButton.Left);
        editorWindow.MouseMove(centre - new Point(0, side / 4));
        editorWindow.MouseUp(centre - new Point(0, side / 4), MouseButton.Left);
        Assert.Equal("Pincel", editor.HistoryItems[^1].Label);

        var before = editor.ZoomPercent;
        editorWindow.MouseWheel(centre, new Avalonia.Vector(0, 2));
        Assert.True(editor.ZoomPercent > before);
    }

    private async Task<string> WritePng(string name, RgbaImage image)
    {
        var path = Path.Combine(_dir, name);
        await using (var file = File.Create(path)) PngWriter.Write(image, file);
        return path;
    }

    private async Task<MainWindow> OpenProjectWithTexture()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "assets"));
        await using (var file = File.Create(Path.Combine(_dir, "assets", "jbib_diff_000_a_uni.ytd")))
            TextureDictionaryWriter.Create([("jbib_diff_000_a_uni", TextureEncoder.Encode(Solid(64, 64, 200, 40, 40), TextureFormat.Dxt1))]).Write(file);
        File.WriteAllText(Path.Combine(_dir, "assets", "jbib_000_u.ydd"), "");
        var path = Path.Combine(_dir, "ui" + ProjectFile.Extension);
        await ProjectFile.SaveAsync(new ClothingProject
        {
            Name = "UI",
            CollectionName = "ui",
            Drawables = [new Drawable { Name = "jbib_000", Component = ComponentSlot.Top, ModelPath = "assets/jbib_000_u.ydd", Textures = [new() { TexturePath = "assets/jbib_diff_000_a_uni.ytd" }] }],
        }, path);
        var window = new MainWindow();
        window.Show();
        window.ViewModel.Settings = new();
        await window.ViewModel.OpenProjectFileAsync(path);
        Assert.True(window.ViewModel.EditTextureCommand.CanExecute(null));
        return window;
    }

    private static async Task Settle(TextureEditorViewModel editor)
    {
        await Task.Delay(30);
        await editor.WaitForComposeAsync();
        Dispatcher.UIThread.RunJobs();
    }

    private static RgbaImage Solid(int w, int h, byte r, byte g, byte b) =>
        new(w, h, Enumerable.Range(0, w * h).SelectMany(_ => new byte[] { r, g, b, 255 }).ToArray());

    private static byte[] Pixel(RgbaImage image, int x, int y) => image.Pixels[((y * image.Width + x) * 4)..((y * image.Width + x) * 4 + 4)];
}
