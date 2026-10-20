using System.Numerics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using EasyClothes.App.ViewModels;
using EasyClothes.App.Viewer;
using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;
using EasyClothes.Core.Textures;

namespace EasyClothes.App.Tests;

public sealed class MainWindowTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("easyclothes-ui-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [AvaloniaFact]
    public async Task Opening_a_project_fills_the_tree_and_reports_missing_files()
    {
        var path = await SaveProject(new ClothingProject
        {
            Name = "UI",
            CollectionName = "ui",
            Drawables =
            [
                new Drawable { Name = "jbib_000", Gender = PedGender.Female, Component = ComponentSlot.Top, ModelPath = "missing.ydd" },
                new Drawable { Name = "p_head_000", Prop = PropSlot.Hat, ModelPath = "missing_hat.ydd" },
            ],
        });
        var window = new MainWindow();
        window.Show();

        await window.ViewModel.OpenProjectFileAsync(path);
        await WaitForAnalysis(window.ViewModel);

        Assert.Equal(["Hombre", "Mujer"], window.ViewModel.Tree.Select(n => n.Title));
        Assert.Equal("p_head_000", window.ViewModel.Selected?.Name);
        Assert.Contains(window.ViewModel.Issues, i => i.IsError && i.Message.Contains("missing.ydd"));
        Assert.Equal("EasyClothes — UI", window.Title);
    }

    [AvaloniaFact]
    public async Task Renaming_keeps_the_selection_and_marks_the_project_dirty()
    {
        var path = await SaveProject(new ClothingProject
        {
            Name = "UI",
            CollectionName = "ui",
            Drawables = [new Drawable { Name = "lowr_000", Component = ComponentSlot.Legs, ModelPath = "a.ydd" }],
        });
        var window = new MainWindow();
        window.Show();
        await window.ViewModel.OpenProjectFileAsync(path);

        window.ViewModel.Selected!.Name = "vaqueros";

        Assert.Equal("vaqueros", window.ViewModel.SelectedNode?.Title);
        Assert.True(window.ViewModel.IsDirty);
        Assert.EndsWith("*", window.Title);
    }

    [AvaloniaFact]
    public async Task Building_reports_errors_then_writes_the_resource_once_fixed()
    {
        File.WriteAllText(Path.Combine(_dir, "a.ydd"), "");
        var path = await SaveProject(new ClothingProject
        {
            Name = "UI",
            CollectionName = "ui",
            Drawables = [new Drawable { Name = "lowr_000", Component = ComponentSlot.Legs, ModelPath = "a.ydd" }],
        });
        var window = new MainWindow();
        window.Show();
        await window.ViewModel.OpenProjectFileAsync(path);
        var output = Path.Combine(_dir, "resource");

        await window.ViewModel.BuildResourceAsync(output);
        Assert.Contains("1 error", window.ViewModel.Status);
        Assert.False(Directory.Exists(output));

        File.WriteAllText(Path.Combine(_dir, "a.ytd"), "");
        window.ViewModel.Project!.Drawables[0].Textures.Add(new TextureVariation { TexturePath = "a.ytd" });
        await window.ViewModel.BuildResourceAsync(output);
        Assert.StartsWith("Recurso generado", window.ViewModel.Status);
        Assert.True(File.Exists(Path.Combine(output, "stream", "mp_m_freemode_01_ui.ymt")));
    }

    [AvaloniaFact]
    public async Task Imported_tattoos_appear_in_the_tree_preview_and_can_be_removed()
    {
        var png = Path.Combine(_dir, "rosa.png");
        using (var file = File.Create(png))
            PngWriter.Write(new RgbaImage(8, 4, Enumerable.Repeat((byte)128, 8 * 4 * 4).ToArray()), file);
        var path = await SaveProject(new ClothingProject { Name = "UI", CollectionName = "ui" });
        var window = new MainWindow { Width = 1440, Height = 900 };
        window.Show();
        await window.ViewModel.OpenProjectFileAsync(path);

        window.ViewModel.ImportTattoos([png]);
        for (var i = 0; i < 100 && window.ViewModel.Preview is null; i++)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal("rosa", window.ViewModel.SelectedTattoo?.Name);
        Assert.Equal("Tatuajes (1)", window.ViewModel.Tree.Single().Title);
        Assert.Equal("8x4", window.ViewModel.Preview?.Status);
        Assert.Equal("Torso (1)", window.ViewModel.Tree.Single().Children.Single().Title);
        window.ViewModel.SelectedTattoo!.Zone = TattooZoneOption.All.Single(z => z.Value == TattooZone.LeftArm);
        Assert.Equal("Brazo izquierdo (1)", window.ViewModel.Tree.Single().Children.Single().Title);
        Assert.Equal("rosa", window.ViewModel.SelectedTattoo?.Name);
        window.ViewModel.SelectedTattoo!.UvX = 0.3m;
        window.ViewModel.SelectedTattoo.IsFemale = false;
        Assert.Equal(0.3f, window.ViewModel.Project!.Tattoos[0].UvX);
        Assert.Equal([PedGender.Male], window.ViewModel.Project.Tattoos[0].Genders);
        Capture(window, Environment.GetEnvironmentVariable("EASYCLOTHES_SCREENSHOTS"), "tattoo.png");

        window.ViewModel.RemoveCommand.Execute(null);
        Assert.Empty(window.ViewModel.Project.Tattoos);
        Assert.Empty(window.ViewModel.Tree);
    }

    [AvaloniaFact]
    public async Task Packing_saves_a_backup_and_the_package_reopens_elsewhere()
    {
        var path = await SaveProject(new ClothingProject { Name = "UI", CollectionName = "ui" });
        var window = new MainWindow();
        window.Show();
        await window.ViewModel.OpenProjectFileAsync(path);
        window.ViewModel.CollectionName = "ui_nueva";
        var package = Path.Combine(_dir, "ui.ecpack");

        await window.ViewModel.PackProjectAsync(package);
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, ProjectFile.BackupsFolder, "project")));

        await window.ViewModel.UnpackProjectAsync(package, Path.Combine(_dir, "copia"));
        Assert.Equal("ui_nueva", window.ViewModel.CollectionName);
        Assert.Equal(Path.Combine(_dir, "copia"), window.ViewModel.ProjectDir);
    }

    [AvaloniaFact]
    public void Dragging_in_the_viewer_orbits_pans_and_double_click_recentres()
    {
        var window = new MainWindow { Width = 1200, Height = 800 };
        window.Show();
        var viewport = window.FindControl<MeshViewport>("Viewport")!;
        var centre = viewport.TranslatePoint(new Point(viewport.Bounds.Width / 2, viewport.Bounds.Height / 2), window)!.Value;

        window.MouseDown(centre, MouseButton.Left);
        window.MouseMove(centre + new Point(80, 30));
        window.MouseUp(centre + new Point(80, 30), MouseButton.Left);
        Assert.NotEqual(0, viewport.Camera.Yaw);

        window.MouseDown(centre, MouseButton.Right);
        window.MouseMove(centre + new Point(50, 0));
        window.MouseUp(centre + new Point(50, 0), MouseButton.Right);
        Assert.NotEqual(Vector3.Zero, viewport.Camera.Target);

        var elsewhere = centre + new Point(40, 40);
        window.MouseDown(elsewhere, MouseButton.Left);
        window.MouseUp(elsewhere, MouseButton.Left);
        window.MouseDown(elsewhere, MouseButton.Left);
        window.MouseUp(elsewhere, MouseButton.Left);
        Assert.Equal(0, viewport.Camera.Yaw);
        Assert.Equal(Vector3.Zero, viewport.Camera.Target);
    }

    // Set EASYCLOTHES_SAMPLES (and optionally EASYCLOTHES_SCREENSHOTS) to run against real assets.
    [AvaloniaFact]
    public async Task Fix_all_clears_every_fixable_issue_on_sample_assets()
    {
        if (Environment.GetEnvironmentVariable("EASYCLOTHES_SAMPLES") is not { } samples) return;
        var output = Environment.GetEnvironmentVariable("EASYCLOTHES_SCREENSHOTS");

        var path = await SaveProject(new ClothingProject { Name = "Muestras", CollectionName = "muestras" });
        var project = await ProjectFile.LoadAsync(path);
        string[] names = ["mp_m_freemode_01_mp_m_eup^jbib_038_u.ydd", "mp_m_freemode_01_mp_m_eup^accs_032_u.ydd", "mp_m_freemode_01_mp_m_eup^feet_001_u.ydd", "mp_m_freemode_01_mp_m_eup^jbib_040_u.ydd"];
        DrawableImporter.Import(project, _dir, names.Select(n => Path.Combine(samples, "stream", n)), PedGender.Male);
        await ProjectFile.SaveAsync(project, path);

        var window = new MainWindow { Width = 1440, Height = 900 };
        window.Show();
        await window.ViewModel.OpenProjectFileAsync(path);
        await WaitForAnalysis(window.ViewModel);
        Assert.True(window.ViewModel.HasFixes);
        Capture(window, output, "before-fix.png");

        await window.ViewModel.FixAllCommand.ExecuteAsync(null);
        await WaitForAnalysis(window.ViewModel);

        Assert.False(window.ViewModel.HasFixes, string.Join("\n", window.ViewModel.Issues.Select(i => i.Message)));
        Assert.True(Directory.Exists(Path.Combine(_dir, ".backups")));
        Capture(window, output, "after-fix.png");
    }

    private static void Capture(MainWindow window, string? dir, string name)
    {
        if (dir is null) return;
        Directory.CreateDirectory(dir);
        window.CaptureRenderedFrame()?.Save(Path.Combine(dir, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    private async Task<string> SaveProject(ClothingProject project)
    {
        var path = Path.Combine(_dir, project.CollectionName + ProjectFile.Extension);
        await ProjectFile.SaveAsync(project, path);
        return path;
    }

    private static async Task WaitForAnalysis(MainWindowViewModel vm)
    {
        for (var i = 0; i < 600 && vm.IsAnalyzing; i++)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }
        Dispatcher.UIThread.RunJobs();
    }
}
