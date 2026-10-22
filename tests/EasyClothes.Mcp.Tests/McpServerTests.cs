using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;
using EasyClothes.Mcp;
using EasyClothes.Mcp.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EasyClothes.Mcp.Tests;

public sealed class McpServerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("easyclothes-mcp-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Windows_and_wsl_paths_resolve_on_the_running_system()
    {
        if (OperatingSystem.IsWindows())
            Assert.Equal(@"D:\GTA Vice\ropa", HostPaths.Normalize("/mnt/d/GTA Vice/ropa"));
        else
        {
            Assert.Equal("/mnt/d/GTA Vice/ropa", HostPaths.Normalize(@"D:\GTA Vice\ropa"));
            Assert.Equal("/mnt/c", HostPaths.Normalize("\"C:\\\""));
            Assert.Equal("/", HostPaths.Normalize("/"));
        }
    }

    [Fact]
    public async Task Project_summary_lists_garments_with_their_in_game_numbers()
    {
        var path = await SaveProject(
            new Drawable { Name = "b", Component = ComponentSlot.Top, ModelPath = "b.ydd", Position = 1 },
            new Drawable { Name = "a", Component = ComponentSlot.Top, ModelPath = "a.ydd", Position = 0 },
            new Drawable { Name = "tacon", Gender = PedGender.Female, Component = ComponentSlot.Shoes, ModelPath = "t.ydd", Flags = { HeelHeight = 1.2f } });
        var tools = new ProjectTools(new Workspace(new ServerOptions([], null)));

        var summary = await tools.GetProject(_dir, default);

        Assert.Equal(path, summary.File);
        Assert.Equal(["a 0", "b 1", "tacon 0"], summary.Garments.Select(g => $"{g.Name} {g.Number}"));
        Assert.Equal(1.2f, summary.Garments[2].HeelHeight);
        Assert.Equal(6, summary.Errors);
    }

    [Fact]
    public async Task Ambiguous_names_and_folders_outside_the_roots_are_refused()
    {
        await SaveProject(
            new Drawable { Name = "gorra", Prop = PropSlot.Hat, ModelPath = "a.ydd" },
            new Drawable { Name = "gorra", Gender = PedGender.Female, Prop = PropSlot.Hat, ModelPath = "b.ydd" });
        var open = new ProjectTools(new Workspace(new ServerOptions([], null)));
        var limited = new ProjectTools(new Workspace(new ServerOptions([Path.Combine(_dir, "otra")], null)));

        var ambiguous = await Assert.ThrowsAsync<McpException>(() => open.GetGarment(_dir, "gorra", default));
        Assert.Contains("usa su id", ambiguous.Message);
        var outside = await Assert.ThrowsAsync<McpException>(() => limited.GetProject(_dir, default));
        Assert.Contains("fuera de las carpetas permitidas", outside.Message);
    }

    [Fact]
    public async Task Server_answers_over_stdio_with_tools_results_and_readable_errors()
    {
        await SaveProject(new Drawable { Name = "chaqueta", Component = ComponentSlot.Top, ModelPath = "missing.ydd" });
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Name = "easyclothes",
            Command = "dotnet",
            Arguments = [typeof(Workspace).Assembly.Location, "--root", _dir],
        }));

        var tools = await client.ListToolsAsync();
        var readOnly = tools.Where(t => t.ProtocolTool.Annotations?.ReadOnlyHint == true).Select(t => t.Name).Order();
        Assert.Equal(["find_projects", "get_garment", "get_project", "render_garment", "validate"], readOnly);
        Assert.True(tools.Single(t => t.Name == "remove_garment").ProtocolTool.Annotations?.DestructiveHint);
        Assert.All(tools, t => Assert.False(string.IsNullOrWhiteSpace(t.Description)));

        var found = await client.CallToolAsync("find_projects", new Dictionary<string, object?> { ["folder"] = _dir });
        Assert.Contains("\"name\":\"Ropa\"", Text(found));

        var issues = await client.CallToolAsync("validate", new Dictionary<string, object?> { ["project"] = _dir });
        Assert.Contains("falta el modelo missing.ydd", Text(issues));

        var render = await client.CallToolAsync("render_garment", new Dictionary<string, object?> { ["project"] = _dir, ["garment"] = "chaqueta" });
        Assert.True(render.IsError);
        Assert.Contains("No se pudo renderizar chaqueta", Text(render));
    }

    // Set EASYCLOTHES_SAMPLES (and optionally EASYCLOTHES_BODY) to render a real garment.
    [Fact]
    public async Task Renders_a_real_garment_as_one_image_with_a_view_per_angle()
    {
        if (Environment.GetEnvironmentVariable("EASYCLOTHES_SAMPLES") is not { } samples) return;
        var path = await SaveProject();
        var project = await ProjectFile.LoadAsync(path);
        EasyClothes.Core.Importing.DrawableImporter.Import(project, _dir, [Path.Combine(samples, "stream", "mp_m_freemode_01_mp_m_eup^jbib_038_u.ydd")], PedGender.Male);
        await ProjectFile.SaveAsync(project, path);
        var tools = new PreviewTools(new Workspace(new ServerOptions([], Environment.GetEnvironmentVariable("EASYCLOTHES_BODY"))));

        var result = (await tools.RenderGarment(_dir, "jbib_038", "b", "front,back", size: 300)).ToList();

        var image = Assert.IsType<ImageContentBlock>(result[0]);
        var png = EasyClothes.Core.Textures.PngReader.Read(new MemoryStream(image.DecodedData.ToArray()));
        Assert.Equal((400, 300), (png.Width, png.Height));
    }

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text));

    private async Task<string> SaveProject(params Drawable[] drawables)
    {
        var path = Path.Combine(_dir, "ropa" + ProjectFile.Extension);
        await ProjectFile.SaveAsync(new ClothingProject { Name = "Ropa", CollectionName = "ropa", Drawables = [.. drawables] }, path);
        return path;
    }
}
