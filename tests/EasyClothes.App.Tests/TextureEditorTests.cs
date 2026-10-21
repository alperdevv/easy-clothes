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
    public async Task Edits_preview_live_and_save_as_a_new_variation()
    {
        var window = await OpenProjectWithTexture();
        var editor = (await window.ViewModel.CreateTextureEditorAsync())!;
        editor.ExternalPath = Path.Combine(_dir, "external");

        editor.Saturation = -100;
        await Settle(editor);
        Assert.Equal(editor.Composed.Pixels[0], editor.Composed.Pixels[1]);

        var logo = Path.Combine(_dir, "logo.png");
        await using (var file = File.Create(logo)) PngWriter.Write(Solid(4, 4, 0, 0, 255), file);
        editor.AddDecal(logo);
        await Settle(editor);
        Assert.Equal([0, 0, 255], Pixel(editor.Composed, 32, 32)[..3]);

        editor.Tool = EditorTool.Brush;
        editor.BrushColor = Colors.Lime;
        editor.BeginStroke();
        editor.StrokeTo(new Vector2(0.1f, 0.1f), new Vector2(0.2f, 0.1f));
        await Settle(editor);
        Assert.Equal([0, 255, 0], Pixel(editor.Composed, 10, 6)[..3]);
        editor.UndoCommand.Execute(null);
        await Settle(editor);
        Assert.NotEqual([0, 255, 0], Pixel(editor.Composed, 10, 6)[..3]);

        await editor.SaveAsync(TextureSaveMode.NewVariation);
        window.ViewModel.ApplyTextureEdit(editor);

        Assert.Equal(2, window.ViewModel.Selected!.Textures.Count);
        Assert.Equal(1, window.ViewModel.SelectedTexture?.Index);
        Assert.True(window.ViewModel.IsDirty);
        var saved = ImageLoader.Load(Path.Combine(_dir, window.ViewModel.SelectedTexture!.RelativePath));
        Assert.True(Pixel(saved, 32, 32)[2] > 200);
    }

    [AvaloniaFact]
    public async Task External_edits_become_the_new_base_and_drop_baked_layers()
    {
        var window = await OpenProjectWithTexture();
        var editor = (await window.ViewModel.CreateTextureEditorAsync())!;
        editor.Hue = 90;
        var png = Path.Combine(_dir, "fuera.png");
        await using (var file = File.Create(png)) PngWriter.Write(Solid(32, 32, 10, 20, 30), file);

        await editor.ReloadExternalAsync(png);
        await Settle(editor);

        Assert.Equal(0, editor.Hue);
        Assert.Equal("32x32", editor.SizeLabel);
        Assert.Equal([10, 20, 30, 255], Pixel(editor.Composed, 5, 5));
        Assert.True(editor.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public async Task Dragging_on_the_canvas_moves_the_selected_logo()
    {
        var window = await OpenProjectWithTexture();
        var editor = (await window.ViewModel.CreateTextureEditorAsync())!;
        var logo = Path.Combine(_dir, "logo.png");
        await using (var file = File.Create(logo)) PngWriter.Write(Solid(4, 4, 255, 0, 0), file);
        editor.AddDecal(logo);

        var editorWindow = new TextureEditorWindow(editor) { Width = 1400, Height = 860 };
        editorWindow.Show();
        Dispatcher.UIThread.RunJobs();
        var canvas = editorWindow.FindControl<TextureCanvas>("Canvas")!;
        var centre = canvas.TranslatePoint(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2), editorWindow)!.Value;
        var side = Math.Min(canvas.Bounds.Width, canvas.Bounds.Height);

        editorWindow.MouseDown(centre, MouseButton.Left);
        editorWindow.MouseMove(centre + new Point(side / 4, 0));
        editorWindow.MouseUp(centre + new Point(side / 4, 0), MouseButton.Left);

        Assert.InRange(editor.SelectedDecal!.Model.X, 0.7f, 0.8f);
        Assert.InRange(editor.SelectedDecal.Model.Y, 0.45f, 0.55f);
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
