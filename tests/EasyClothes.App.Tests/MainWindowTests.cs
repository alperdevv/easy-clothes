using System.Numerics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EasyClothes.App.ViewModels;
using EasyClothes.App.Viewer;
using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;
using EasyClothes.Core.Settings;
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
    public async Task Dropping_a_folder_without_a_project_imports_into_a_draft_that_saves_elsewhere()
    {
        var pack = Directory.CreateDirectory(Path.Combine(_dir, "pack", "mp_f_freemode_01_x")).FullName;
        File.WriteAllText(Path.Combine(pack, "lowr_003_u.ydd"), "");
        File.WriteAllText(Path.Combine(pack, "lowr_diff_003_a_uni.ytd"), "");
        File.WriteAllText(Path.Combine(pack, "notas.txt"), "");
        var window = new MainWindow();
        window.Show();
        window.ViewModel.DraftsRoot = Path.Combine(_dir, "drafts");

        await window.ViewModel.DropAsync([Path.Combine(_dir, "pack")]);

        Assert.True(window.ViewModel.IsDraft);
        Assert.Equal("EasyClothes — Sin guardar *", window.Title);
        Assert.Equal("lowr_003", window.ViewModel.Selected?.Name);
        Assert.Equal("Mujer", window.ViewModel.Tree.Single().Title);

        Assert.True(await window.ViewModel.SaveDraftAsAsync(Path.Combine(_dir, "Mi Ropa")));
        await WaitForAnalysis(window.ViewModel);

        Assert.False(window.ViewModel.IsDraft);
        Assert.Equal("mi_ropa", window.ViewModel.CollectionName);
        Assert.True(File.Exists(Path.Combine(_dir, "Mi Ropa", "mi_ropa.ecproj")));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(_dir, "drafts")));
        Assert.DoesNotContain(window.ViewModel.Issues, i => i.Message.Contains("falta"));
    }

    [AvaloniaFact]
    public async Task Shoes_show_heels_hats_show_hair_and_extras_reach_the_project_file()
    {
        File.WriteAllText(Path.Combine(_dir, "feet.ydd"), "");
        File.WriteAllText(Path.Combine(_dir, "jacket_fp.ydd"), "fp");
        var path = await SaveProject(new ClothingProject
        {
            Name = "UI",
            CollectionName = "ui",
            Drawables =
            [
                new Drawable { Name = "tacones", Gender = PedGender.Female, Component = ComponentSlot.Shoes, ModelPath = "feet.ydd" },
                new Drawable { Name = "gorra", Gender = PedGender.Female, Prop = PropSlot.Hat, ModelPath = "hat.ydd" },
                new Drawable { Name = "chaqueta", Gender = PedGender.Female, Component = ComponentSlot.Top, ModelPath = "models/jacket.ydd" },
            ],
        });
        var window = new MainWindow();
        window.Show();
        var vm = window.ViewModel;
        await vm.OpenProjectFileAsync(path);

        SelectByName(vm, "tacones");
        Dispatcher.UIThread.RunJobs();
        var heels = window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Tacones"));
        Assert.True(heels.IsEffectivelyVisible);
        heels.IsChecked = true;
        vm.Selected!.HeelHeight = 1.4m;
        await vm.DetectHeelCommand.ExecuteAsync(null);
        Assert.Contains("No se pudo leer", vm.Status);

        SelectByName(vm, "gorra");
        Dispatcher.UIThread.RunJobs();
        Assert.False(heels.IsEffectivelyVisible);
        vm.Selected!.HatHairIndex = (int)HatHair.Remove;

        SelectByName(vm, "chaqueta");
        DrawableImporter.AttachFirstPerson(vm.ProjectDir!, vm.Selected!.Model, Path.Combine(_dir, "jacket_fp.ydd"));
        vm.Selected.RefreshExtras();
        Assert.Equal("jacket_1.ydd", vm.Selected.FirstPersonLabel);
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = await ProjectFile.LoadAsync(path);
        Assert.Equal(1.4f, saved.Drawables.Single(d => d.Name == "tacones").Flags.HeelHeight);
        Assert.Equal(HatHair.Remove, saved.Drawables.Single(d => d.Name == "gorra").Flags.HatHair);
        Assert.Equal("models/jacket_1.ydd", saved.Drawables.Single(d => d.Name == "chaqueta").FirstPersonModelPath);
        Assert.Equal("fp", File.ReadAllText(Path.Combine(_dir, "models", "jacket_1.ydd")));
    }

    [AvaloniaFact]
    public async Task Garments_can_be_reordered_and_numbers_reserved_from_the_panel()
    {
        var path = await SaveProject(new ClothingProject
        {
            Name = "UI",
            CollectionName = "ui",
            Drawables =
            [
                new Drawable { Name = "a", Component = ComponentSlot.Top, ModelPath = "a.ydd" },
                new Drawable { Name = "b", Component = ComponentSlot.Top, ModelPath = "b.ydd" },
                new Drawable { Name = "c", Component = ComponentSlot.Top, ModelPath = "c.ydd" },
            ],
        });
        var window = new MainWindow();
        window.Show();
        var vm = window.ViewModel;
        await vm.OpenProjectFileAsync(path);
        SelectByName(vm, "c");

        window.KeyPress(Key.Up, RawInputModifiers.Alt, PhysicalKey.ArrowUp, null);
        Assert.Equal(["000 a", "001 c", "002 b"], TopNodes(vm));
        Assert.Equal("Número en el juego: 001", vm.Selected?.NumberLabel);
        Assert.True(vm.IsDirty);

        vm.ReserveSlotCommand.Execute(null);
        Assert.Equal(["000 a", "001 c", "002 b", "003 reservado 1"], TopNodes(vm));
        Assert.True(vm.Selected?.IsReserved);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Asignar prenda…") && b.IsEffectivelyVisible);
        Assert.DoesNotContain(vm.Issues, i => i.Message.Contains("reservado"));
    }

    [AvaloniaFact]
    public async Task Dropping_a_finished_resource_keeps_numbers_and_takes_its_collection()
    {
        var stream = Directory.CreateDirectory(Path.Combine(_dir, "recurso", "stream")).FullName;
        EasyClothes.Core.Rage.PedVariation.Write("mp_f_tienda",
            new Dictionary<ComponentSlot, IReadOnlyList<EasyClothes.Core.Rage.VariationDrawable>> { [ComponentSlot.Legs] = [new(1), new(1)] },
            new Dictionary<PropSlot, IReadOnlyList<EasyClothes.Core.Rage.VariationDrawable>>())
            .Write(Path.Combine(stream, "mp_f_freemode_01_mp_f_tienda.ymt"));
        foreach (var name in new[] { "mp_f_freemode_01_mp_f_tienda^lowr_001_u.ydd", "mp_f_freemode_01_mp_f_tienda^lowr_diff_001_a_uni.ytd" })
            File.WriteAllText(Path.Combine(stream, name), "");
        var window = new MainWindow();
        window.Show();
        window.ViewModel.DraftsRoot = Path.Combine(_dir, "drafts");

        await window.ViewModel.DropAsync([Path.Combine(_dir, "recurso")]);

        Assert.Equal("tienda", window.ViewModel.CollectionName);
        Assert.Equal(["000 reservado 1", "001 lowr_001"], TopNodes(window.ViewModel));
        Assert.StartsWith("1 prenda(s) importada(s).", window.ViewModel.Status);
    }

    [AvaloniaFact]
    public async Task Changes_made_outside_the_app_reload_the_open_project()
    {
        var path = await SaveProject(new ClothingProject
        {
            Name = "UI",
            CollectionName = "ui",
            Drawables = [new Drawable { Name = "a", Component = ComponentSlot.Top, ModelPath = "a.ydd" }],
        });
        var window = new MainWindow();
        window.Show();
        var vm = window.ViewModel;
        await vm.OpenProjectFileAsync(path);

        var outside = await ProjectFile.LoadAsync(path);
        outside.Drawables.Add(new Drawable { Name = "b", Component = ComponentSlot.Top, ModelPath = "b.ydd", Position = 1 });
        await ProjectFile.SaveAsync(outside, path);
        for (var i = 0; i < 100 && !vm.Status.Contains("desde fuera"); i++)
        {
            await Task.Delay(30);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal(["000 a", "001 b"], TopNodes(vm));
        Assert.False(vm.IsDirty);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Single(Directory.GetFiles(_dir, "*" + ProjectFile.Extension));
    }

    [AvaloniaFact]
    public async Task Connecting_claude_explains_when_the_server_is_missing()
    {
        var window = new MainWindow();
        window.Show();
        window.ViewModel.McpServerPath = Path.Combine(_dir, "EasyClothes.Mcp.exe");

        await window.ViewModel.ConnectClaudeCommand.ExecuteAsync(null);

        Assert.StartsWith("No se encuentra EasyClothes.Mcp.exe", window.ViewModel.Status);
    }

    [AvaloniaFact]
    public void Choosing_the_body_folder_persists_and_can_be_turned_off()
    {
        var window = new MainWindow();
        window.Show();
        window.ViewModel.SettingsPath = Path.Combine(_dir, "settings.json");
        window.ViewModel.Settings = new();
        Assert.False(window.ViewModel.ShowBody);

        Assert.False(window.ViewModel.SetBodyFolder(_dir));
        Directory.CreateDirectory(Path.Combine(_dir, "mp_f_freemode_01"));
        Assert.True(window.ViewModel.SetBodyFolder(Path.Combine(_dir, "mp_f_freemode_01")));

        Assert.True(window.ViewModel.ShowBody);
        Assert.Equal(_dir, AppSettings.Load(window.ViewModel.SettingsPath).BaseBodyFolder);
        window.ViewModel.ShowBody = false;
        Assert.False(AppSettings.Load(window.ViewModel.SettingsPath).ShowBody);
    }

    // Set EASYCLOTHES_BODY to the folder with the exported freemode peds to check the mannequin loads.
    [AvaloniaFact]
    public async Task Selected_garment_is_shown_on_the_exported_body()
    {
        if (Environment.GetEnvironmentVariable("EASYCLOTHES_BODY") is not { } body) return;
        var model = Path.Combine(body, "mp_m_freemode_01", "jbib_000_u.ydd");
        var window = new MainWindow();
        window.Show();
        window.ViewModel.SettingsPath = Path.Combine(_dir, "settings.json");
        window.ViewModel.Settings = new();
        window.ViewModel.DraftsRoot = Path.Combine(_dir, "drafts");
        Assert.True(window.ViewModel.SetBodyFolder(body));

        await window.ViewModel.ImportDrawablesAsync([model]);
        for (var i = 0; i < 200 && window.ViewModel.Preview?.Body.Count is null or 0; i++)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal(5, window.ViewModel.Preview?.Body.Count);
        Assert.All(window.ViewModel.Preview!.Body, part => Assert.NotNull(part.Texture));
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

    private static List<string> TopNodes(MainWindowViewModel vm) =>
        vm.Tree.SelectMany(g => g.Children).SelectMany(s => s.Children).Select(n => $"{n.Number} {n.Title}").ToList();

    private static void SelectByName(MainWindowViewModel vm, string name) =>
        vm.SelectedNode = vm.Tree.SelectMany(g => g.Children).SelectMany(s => s.Children).Single(n => n.Title == name);

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
