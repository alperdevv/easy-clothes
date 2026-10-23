using EasyClothes.Mcp.Tools;
using ModelContextProtocol;

namespace EasyClothes.Mcp.Tests;

public sealed class CapToolsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("easyclothes-mcp-cap-").FullName;
    private readonly EditTools _edit;
    private readonly CapTools _caps;

    public CapToolsTests()
    {
        var workspace = new Workspace(new ServerOptions([], null));
        _edit = new EditTools(workspace);
        _caps = new CapTools(workspace);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Claude_can_create_reshape_and_recolour_a_cap()
    {
        var project = Path.Combine(_root, "Gorras");
        await _edit.CreateProject(project, "Gorras");

        var created = await _caps.CreateCap(project, "mujer", "Snapback", visorLengthCm: 6, frontColor: "#ffffff");
        Assert.Equal(("Snapback", "mujer", "p_head", 1), (created.Garment.Name, created.Garment.Gender, created.Garment.SlotCode, created.Garment.Textures));
        Assert.Equal(("#FFFFFF", "#1E1F24"), (created.Colours.Front, created.Colours.Crown));

        var reshaped = await _caps.UpdateCap(project, "Snapback", "shape", style: "trucker", rotation: 90);
        Assert.Equal(("trucker", 90f), (reshaped.Shape.Style, reshaped.Shape.Rotation));
        Assert.Equal(7, reshaped.Shape.VisorLengthCm);

        var recoloured = await _caps.UpdateCap(project, "Snapback", "colours", crownColor: "#AA0000");
        Assert.Equal((2, "#AA0000", "#FFFFFF"), (recoloured.Garment.Textures, recoloured.Colours.Crown, recoloured.Colours.Front));

        var error = await Assert.ThrowsAsync<McpException>(() => _caps.UpdateCap(project, "Snapback", "colours", crownColor: "rojo"));
        Assert.Contains("#RRGGBB", error.Message);

        var beanie = await _caps.CreateCap(project, "mujer", "Gorro", style: "gorro", cuffHeightCm: 3, pompom: true);
        Assert.Equal(("gorro", 3f, true), (beanie.Shape.Style, beanie.Shape.CuffHeightCm, beanie.Shape.Pompom));
        Assert.Contains("doblez", beanie.TextureLayout);
        var mismatch = await Assert.ThrowsAsync<McpException>(() => _caps.UpdateCap(project, "Gorro", "shape", style: "bucket"));
        Assert.Contains("create_cap", mismatch.Message);
    }
}
