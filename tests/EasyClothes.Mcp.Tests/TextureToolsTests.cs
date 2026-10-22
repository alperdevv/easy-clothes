using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Textures.Editing;
using EasyClothes.Mcp.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace EasyClothes.Mcp.Tests;

public sealed class TextureToolsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("easyclothes-mcp-tex-").FullName;
    private readonly Workspace _workspace = new(new ServerOptions([], null));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Tattoos_are_imported_placed_and_removed()
    {
        await Save(new ClothingProject { Name = "T", CollectionName = "t" });
        var png = Path.Combine(_dir, "rosa.png");
        await using (var file = File.Create(png)) PngWriter.Write(new RgbaImage(8, 8, new byte[256]), file);
        var tools = new TattooTools(_workspace);

        var imported = await tools.ImportTattoos(_dir, [png], "LeftArm");
        var placed = await tools.UpdateTattoo(_dir, "rosa", zone: "RightLeg", genders: "mujer", uvX: 0.3f, rotation: 15);
        Assert.Equal(("rosa", "LeftArm"), (imported.Added[0].Name, imported.Added[0].Zone));
        Assert.Equal(("RightLeg", "mujer", 0.3f, 15f), (placed.Zone, placed.Genders.Single(), placed.UvX, placed.Rotation));
        Assert.Contains("Zona desconocida", (await Assert.ThrowsAsync<McpException>(() => tools.UpdateTattoo(_dir, "rosa", zone: "pecho"))).Message);
        Assert.Contains("quedan 0", await tools.RemoveTattoo(_dir, "rosa"));
    }

    [Fact]
    public async Task Bad_operations_are_rejected_before_anything_is_saved()
    {
        var ytd = Path.Combine(_dir, "a.ytd");
        TextureDictionaryWriter.Create([("a", TextureEncoder.Encode(new RgbaImage(8, 8, Enumerable.Repeat((byte)128, 256).ToArray()), TextureFormat.Dxt5))]).Write(ytd);
        await Save(new ClothingProject
        {
            Name = "T", CollectionName = "t",
            Drawables = [new Drawable { Name = "top", Component = ComponentSlot.Top, ModelPath = "a.ydd", Textures = [new() { TexturePath = "a.ytd" }] }],
        });
        var tools = new TextureTools(_workspace);

        Assert.Contains("Operación desconocida", (await Assert.ThrowsAsync<McpException>(() => tools.EditTexture(_dir, "top", [new("blur")]))).Message);
        Assert.Contains("#RRGGBB", (await Assert.ThrowsAsync<McpException>(() => tools.EditTexture(_dir, "top", [new("recolor", Color: "rojo")]))).Message);
        Assert.Contains("no existe la 'c'", (await Assert.ThrowsAsync<McpException>(() => tools.EditTexture(_dir, "top", [new("adjust")], texture: "c"))).Message);
        Assert.Single((await ProjectFile.LoadAsync(Path.Combine(_dir, "t.ecproj"))).Drawables[0].Textures);
    }

    // Set EASYCLOTHES_SAMPLES to edit a real garment.
    [Fact]
    public async Task Recoloring_a_real_garment_adds_a_layered_variation_and_shows_it()
    {
        if (Environment.GetEnvironmentVariable("EASYCLOTHES_SAMPLES") is not { } samples) return;
        var project = new ClothingProject { Name = "T", CollectionName = "t" };
        DrawableImporter.Import(project, _dir, [Path.Combine(samples, "stream", "mp_m_freemode_01_mp_m_eup^jbib_038_u.ydd")], PedGender.Male);
        await Save(project);
        var tools = new TextureTools(_workspace);

        var layout = (await tools.RenderUvLayout(_dir, "jbib_038")).ToList();
        Assert.IsType<ImageContentBlock>(layout[0]);

        var preview = (await tools.EditTexture(_dir, "jbib_038", [new("recolor", Color: "#B00020")], save: false)).ToList();
        Assert.Contains("sin guardar", ((TextContentBlock)preview[1]).Text);
        Assert.Equal(2, (await ProjectFile.LoadAsync(Path.Combine(_dir, "t.ecproj"))).Drawables[0].Textures.Count);

        var result = (await tools.EditTexture(_dir, "jbib_038", [new("recolor", Color: "#B00020"), new("adjust", Brightness: 10)])).ToList();
        Assert.Contains("textura nueva 'c'", ((TextContentBlock)result[1]).Text);
        var saved = (await ProjectFile.LoadAsync(Path.Combine(_dir, "t.ecproj"))).Drawables[0].Textures[2].TexturePath;
        var red = GarmentPreviewTexture(Path.Combine(_dir, saved));
        Assert.True(red.R > red.G + 30, $"{red}");
        Assert.True(File.Exists(TextureDocumentFile.PathFor(Path.Combine(_dir, saved))));
    }

    private static (int R, int G, int B) GarmentPreviewTexture(string ytd)
    {
        var image = EasyClothes.Core.Viewing.GarmentPreview.LoadTexture(ytd, out _)!;
        long r = 0, g = 0, b = 0;
        for (var i = 0; i < image.Pixels.Length; i += 4) (r, g, b) = (r + image.Pixels[i], g + image.Pixels[i + 1], b + image.Pixels[i + 2]);
        var n = image.Pixels.Length / 4;
        return ((int)(r / n), (int)(g / n), (int)(b / n));
    }

    private async Task Save(ClothingProject project) => await ProjectFile.SaveAsync(project, Path.Combine(_dir, "t.ecproj"));
}
