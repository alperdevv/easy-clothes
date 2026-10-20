using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using EasyClothes.App.ViewModels;
using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;

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
