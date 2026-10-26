using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;
using EasyClothes.Mcp.Tools;
using ModelContextProtocol;

namespace EasyClothes.Mcp.Tests;

public sealed class EditToolsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("easyclothes-mcp-edit-").FullName;
    private readonly EditTools _edit;
    private readonly ProjectTools _read;

    public EditToolsTests()
    {
        var workspace = new Workspace(new ServerOptions([], null));
        _edit = new EditTools(workspace);
        _read = new ProjectTools(workspace);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Claude_can_build_a_pack_from_loose_files_end_to_end()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "descargas", "mp_f_freemode_01_x")).FullName;
        foreach (var name in new[] { "jbib_000_u.ydd", "jbib_diff_000_a_uni.ytd", "jbib_001_u.ydd", "jbib_diff_001_a_uni.ytd", "jbib_001_u_1.ydd",
                     "feet_000_u.ydd", "feet_diff_000_a_uni.ytd", "p_head_000.ydd", "p_head_diff_000_a.ytd", "lowr_004_u.ydd", "lowr_diff_004_a_uni.ytd" })
            File.WriteAllText(Path.Combine(source, name), name);
        var project = Path.Combine(_root, "Tienda");

        var created = await _edit.CreateProject(project, "Tienda Vice");
        Assert.Equal("tienda_vice", created.Collection);
        var imported = await _edit.ImportGarments(project, [Path.GetDirectoryName(source)!]);
        Assert.Equal(5, imported.Added.Count);
        Assert.True(imported.Added.Single(g => g.Name == "jbib_001").FirstPerson);

        await _edit.UpdateGarment(project, "feet_000", heelHeight: 1.2f);
        await _edit.UpdateGarment(project, "p_head_000", hatHair: "remove");
        var moved = await _edit.MoveGarment(project, "jbib_001", 0);
        Assert.Equal(["000 jbib_001", "001 jbib_000"], moved.Garments);
        var reserved = await _edit.ReserveNumbers(project, "mujer", "jbib", 2);
        Assert.Equal(["000 jbib_001", "001 jbib_000", "002 reservado 1", "003 reservado 2"], reserved.Garments);
        var filled = await _edit.FillReserved(project, "reservado 1", Path.Combine(source, "lowr_004_u.ydd"));
        Assert.Equal(("Top", 2), (filled.Slot, filled.Number));
        await _edit.RemoveGarment(project, "lowr_004");
        await _edit.SetGarmentFile(project, "jbib_001", "first_person");

        var summary = await _read.GetProject(project, default);
        Assert.Equal(0, summary.Errors);
        Assert.Equal(1.2f, summary.Garments.Single(g => g.Name == "feet_000").HeelHeight);
        Assert.Equal("Remove", summary.Garments.Single(g => g.Name == "p_head_000").HatHair);
        Assert.False(summary.Garments.Single(g => g.Name == "jbib_001").FirstPerson);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(project, ProjectFile.BackupsFolder, "project")));

        var build = await _edit.BuildResource(project);
        Assert.EndsWith(Path.Combine("build", "tienda_vice"), build.Output);
        Assert.True(File.Exists(Path.Combine(build.Output, "stream", "mp_creaturemetadata_f_tienda_vice.ymt")));
        Assert.Contains("ensure tienda_vice", build.Install);
        Assert.Contains("archivos empaquetados", await _edit.PackProject(project));
    }

    [Fact]
    public async Task Mistakes_come_back_as_clear_messages()
    {
        var project = Path.Combine(_root, "p");
        await _edit.CreateProject(project, "P");
        await File.WriteAllTextAsync(Path.Combine(_root, "jbib_000_u.ydd"), "");
        await _edit.ImportGarments(project, [Path.Combine(_root, "jbib_000_u.ydd")]);

        Assert.Contains("no es calzado", (await Assert.ThrowsAsync<McpException>(() => _edit.UpdateGarment(project, "jbib_000", heelHeight: 1))).Message);
        Assert.Contains("Hueco desconocido", (await Assert.ThrowsAsync<McpException>(() => _edit.UpdateGarment(project, "jbib_000", slot: "camiseta"))).Message);
        Assert.Contains("no tiene texturas", (await Assert.ThrowsAsync<McpException>(() => _edit.BuildResource(project))).Message);
        Assert.Contains("Ya hay un proyecto", (await Assert.ThrowsAsync<McpException>(() => _edit.CreateProject(project, "Otro"))).Message);
    }

    [Fact]
    public async Task Optimize_folder_lists_first_and_then_fixes_a_resource_without_project()
    {
        var stream = Directory.CreateDirectory(Path.Combine(_root, "gz", "stream")).FullName;
        var ytd = Path.Combine(stream, "jbib_diff_000_a_uni.ytd");
        TextureDictionaryWriter.Create([("a", TextureEncoder.Encode(new RgbaImage(12, 8, Enumerable.Repeat((byte)128, 12 * 8 * 4).ToArray()), TextureFormat.Dxt5))]).Write(ytd);
        var backup = Path.Combine(_root, "copia");

        var plan = await _edit.OptimizeFolder(Path.Combine(_root, "gz"), dryRun: true, backup: backup);
        Assert.Equal((1, 1, (long?)null), (plan.Scanned, plan.Changed, plan.MemoryAfterKiB));
        Assert.False(Directory.Exists(backup));

        var done = await _edit.OptimizeFolder(Path.Combine(_root, "gz"), backup: backup);
        Assert.Equal(backup, done.Backup);
        Assert.Contains("12x8", Assert.Single(done.Changes));
        Assert.True(File.Exists(Path.Combine(backup, "stream", "jbib_diff_000_a_uni.ytd")));
        await Assert.ThrowsAsync<McpException>(() => _edit.OptimizeFolder(Path.Combine(_root, "gz"), backup: Path.Combine(stream, "copia")));
    }
}
