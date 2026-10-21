using System.IO.Compression;
using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;

namespace EasyClothes.Core.Tests;

public sealed class ProjectPackageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("easyclothes-pack-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Package_roundtrips_the_project_and_assets_without_backups()
    {
        var source = Directory.CreateDirectory(Path.Combine(_dir, "src")).FullName;
        var projectPath = Path.Combine(source, "ropa.ecproj");
        await ProjectFile.SaveAsync(new ClothingProject { Name = "Ropa", CollectionName = "ropa" }, projectPath);
        Directory.CreateDirectory(Path.Combine(source, "assets", "male"));
        File.WriteAllText(Path.Combine(source, "assets", "male", "a.ydd"), "modelo");
        Directory.CreateDirectory(Path.Combine(source, ProjectFile.BackupsFolder));
        File.WriteAllText(Path.Combine(source, ProjectFile.BackupsFolder, "old.ytd"), "");
        var package = Path.Combine(_dir, "ropa" + ProjectPackage.Extension);

        Assert.Equal(2, ProjectPackage.Export(projectPath, package));
        var imported = ProjectPackage.Import(package, Path.Combine(_dir, "dst"));

        Assert.Equal("Ropa", (await ProjectFile.LoadAsync(imported)).Name);
        Assert.Equal("modelo", File.ReadAllText(Path.Combine(_dir, "dst", "assets", "male", "a.ydd")));
        Assert.False(Directory.Exists(Path.Combine(_dir, "dst", ProjectFile.BackupsFolder)));
    }

    [Fact]
    public void Draft_moves_to_the_chosen_folder_but_never_over_existing_files()
    {
        var draft = ProjectDraft.Create(Path.Combine(_dir, "drafts"));
        Directory.CreateDirectory(Path.Combine(draft, "assets"));
        File.WriteAllText(Path.Combine(draft, "assets", "a.ydd"), "modelo");
        var busy = Directory.CreateDirectory(Path.Combine(_dir, "ocupada", "assets")).FullName;
        File.WriteAllText(Path.Combine(busy, "a.ydd"), "otro");

        Assert.Throws<IOException>(() => ProjectDraft.MoveTo(draft, Path.Combine(_dir, "ocupada")));
        Assert.Equal("otro", File.ReadAllText(Path.Combine(busy, "a.ydd")));

        ProjectDraft.MoveTo(draft, Path.Combine(_dir, "nueva"));
        Assert.Equal("modelo", File.ReadAllText(Path.Combine(_dir, "nueva", "assets", "a.ydd")));
        Assert.False(Directory.Exists(draft));
    }

    [Fact]
    public void Import_rejects_entries_that_escape_the_target_folder()
    {
        var package = Path.Combine(_dir, "malo.ecpack");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            zip.CreateEntry("p.ecproj");
            zip.CreateEntry("../fuera.txt");
        }

        Assert.Throws<InvalidDataException>(() => ProjectPackage.Import(package, Path.Combine(_dir, "dst")));
        Assert.False(File.Exists(Path.Combine(_dir, "fuera.txt")));
    }

    [Fact]
    public void Backups_keep_only_the_newest_copies()
    {
        var projectPath = Path.Combine(_dir, "p.ecproj");
        File.WriteAllText(projectPath, "{}");
        var dir = Path.Combine(_dir, ProjectFile.BackupsFolder, "project");
        Directory.CreateDirectory(dir);
        for (var i = 0; i < ProjectPackage.KeptBackups; i++)
            File.WriteAllText(Path.Combine(dir, $"p-20000101-0000{i:00}-000.ecproj"), "");

        var newest = ProjectPackage.BackupProjectFile(projectPath);

        var kept = Directory.GetFiles(dir);
        Assert.Equal(ProjectPackage.KeptBackups, kept.Length);
        Assert.Contains(newest, kept);
        Assert.DoesNotContain(kept, f => f.EndsWith("p-20000101-000000-000.ecproj"));
    }
}
