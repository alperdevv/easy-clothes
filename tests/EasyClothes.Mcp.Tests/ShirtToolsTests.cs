using EasyClothes.Mcp.Tools;
using ModelContextProtocol;

namespace EasyClothes.Mcp.Tests;

public sealed class ShirtToolsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("easyclothes-mcp-shirt-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Shirts_need_the_exported_body_and_say_where_to_set_it()
    {
        var workspace = new Workspace(new ServerOptions([], null));
        var project = Path.Combine(_root, "Camis");
        await new EditTools(workspace).CreateProject(project, "Camis");

        var error = await Assert.ThrowsAsync<McpException>(() => new ShirtTools(workspace).CreateShirt(project));

        Assert.Contains("--body", error.Message);
    }

    // Set EASYCLOTHES_BODY to the folder with the exported freemode peds.
    [Fact]
    public async Task Claude_can_create_reshape_and_recolour_a_shirt()
    {
        if (Environment.GetEnvironmentVariable("EASYCLOTHES_BODY") is not { } body) return;
        var workspace = new Workspace(new ServerOptions([], body));
        var project = Path.Combine(_root, "Camis");
        await new EditTools(workspace).CreateProject(project, "Camis");
        var shirts = new ShirtTools(workspace) { Templates = new EasyClothes.Core.Modeling.ShirtTemplates(Path.Combine(_root, "plantillas")) };

        var created = await shirts.CreateShirt(project, "mujer", "Básica", style: "larga", neckline: "pico", bodyColor: "#202020",
            prints: [new ShirtPrint("espalda", Text: "10", Color: "#ffffff", SizeCm: 18)], extraColors: ["#ff0000"]);
        var reshaped = await shirts.UpdateShirt(project, "Básica", "shape", style: "polo", lengthCm: -6, pocket: true);
        var recoloured = await shirts.UpdateShirt(project, "Básica", "colours", bodyColor: "#ff0000", pattern: "rayas", patternColor: "#ffffff", prints: []);
        var saved = await shirts.SaveShirtTemplate(project, "Básica", "Rayada");
        var fromTemplate = await shirts.CreateShirt(project, "hombre", "Copia", template: "Rayada", layer: "interior");

        Assert.Equal(("jbib", "larga", "pico", "#202020", "#202020", 2), (created.Garment.SlotCode, created.Shape.Style, created.Shape.Neckline, created.Look.Body, created.Look.Sleeves, created.Garment.Textures));
        Assert.Equal("10", Assert.Single(created.Look.Prints).Text);
        Assert.Equal(("polo", "polo", -6f, true), (reshaped.Shape.Style, reshaped.Shape.Collar, reshaped.Shape.LengthCm, reshaped.Shape.Pocket));
        Assert.Equal(("#FF0000", "rayas", 3), (recoloured.Look.Body, recoloured.Look.Pattern, recoloured.Garment.Textures));
        Assert.Empty(recoloured.Look.Prints);
        Assert.Equal(("Rayada", "polo"), (saved.Name, saved.Style));
        Assert.Equal(("accs", "interior", "rayas"), (fromTemplate.Garment.SlotCode, fromTemplate.Shape.Layer, fromTemplate.Look.Pattern));
        await Assert.ThrowsAsync<McpException>(() => shirts.CreateShirt(project, style: "frac"));
        await Assert.ThrowsAsync<McpException>(() => shirts.CreateShirt(project, prints: [new ShirtPrint("pecho")]));
    }
}
