using System.Xml.Linq;
using EasyClothes.Core.Export;
using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Tests;

public sealed class ResourceExporterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("easyclothes-export-").FullName;
    private string Output => Path.Combine(_dir, "out", "ropa");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Export_renames_assets_in_game_order_and_writes_the_metadata()
    {
        var project = Project(
            Item("zeta", PedGender.Male, ComponentSlot.Top, textures: 2),
            Item("alfa", PedGender.Male, ComponentSlot.Top, textures: 1),
            Item("gorra", PedGender.Male, prop: PropSlot.Hat, textures: 3),
            Item("falda", PedGender.Female, ComponentSlot.Legs, textures: 1));

        var result = ResourceExporter.Export(project, _dir, Output);

        var male = Path.Combine(Output, "stream", "mp_m_freemode_01");
        Assert.Equal("alfa.ydd", File.ReadAllText(Path.Combine(male, "mp_m_freemode_01_ropa^jbib_000_u.ydd")));
        Assert.Equal("zeta.ydd", File.ReadAllText(Path.Combine(male, "mp_m_freemode_01_ropa^jbib_001_u.ydd")));
        Assert.Equal("zeta_b.ytd", File.ReadAllText(Path.Combine(male, "mp_m_freemode_01_ropa^jbib_diff_001_b_uni.ytd")));
        Assert.True(File.Exists(Path.Combine(male, "mp_m_freemode_01_p_ropa^p_head_000.ydd")));
        Assert.True(File.Exists(Path.Combine(male, "mp_m_freemode_01_p_ropa^p_head_diff_000_c.ytd")));
        Assert.True(File.Exists(Path.Combine(Output, "stream", "mp_f_freemode_01", "mp_f_freemode_01_ropa^lowr_diff_000_a_uni.ytd")));

        var variation = PedVariation.Read(ResourceFile.Read(Path.Combine(Output, "stream", "mp_m_freemode_01_ropa.ymt")));
        Assert.Equal([1, 2], variation.Components[ComponentSlot.Top]);
        Assert.Equal([3], variation.Props[PropSlot.Hat]);
        Assert.Equal(Jenkins.Hash("ropa"), variation.DlcNameHash);

        var shopPath = Path.Combine(Output, "mp_f_freemode_01_ropa.meta");
        Assert.Equal((byte)'<', File.ReadAllBytes(shopPath)[0]);
        var shop = XDocument.Load(shopPath).Root!;
        Assert.Equal("mp_f_freemode_01_ropa", shop.Element("fullDlcName")?.Value);
        var manifest = File.ReadAllText(Path.Combine(Output, "fxmanifest.lua"));
        Assert.Contains("data_file 'SHOP_PED_APPAREL_META_FILE' 'mp_m_freemode_01_ropa.meta'", manifest);
        Assert.Contains("data_file 'SHOP_PED_APPAREL_META_FILE' 'mp_f_freemode_01_ropa.meta'", manifest);
        Assert.Equal(4, result.Drawables);
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(Output)!, ".*"));
    }

    [Fact]
    public void Export_refuses_projects_with_errors()
    {
        var project = Project(Item("rota", PedGender.Male, ComponentSlot.Top, textures: 1));
        project.Drawables[0].Textures.Add(new TextureVariation { TexturePath = "no-existe.ytd" });

        var error = Assert.Throws<ExportException>(() => ResourceExporter.Export(project, _dir, Output));

        Assert.Contains(error.Errors, e => e.Contains("no-existe.ytd"));
        Assert.False(Directory.Exists(Output));
    }

    [Fact]
    public void Export_replaces_its_own_output_but_not_foreign_folders()
    {
        var project = Project(Item("a", PedGender.Male, ComponentSlot.Top, textures: 1));
        ResourceExporter.Export(project, _dir, Output);
        var stale = Path.Combine(Output, "stream", "viejo.ydd");
        File.WriteAllText(stale, "");

        ResourceExporter.Export(project, _dir, Output);
        Assert.False(File.Exists(stale));

        var foreign = Directory.CreateDirectory(Path.Combine(_dir, "otro")).FullName;
        File.WriteAllText(Path.Combine(foreign, "fxmanifest.lua"), "fx_version 'cerulean'");
        Assert.Throws<IOException>(() => ResourceExporter.Export(project, _dir, foreign));
        Assert.True(File.Exists(Path.Combine(foreign, "fxmanifest.lua")));
    }

    [Fact]
    public void Tattoos_become_texture_dictionaries_and_an_overlay_collection()
    {
        var png = Path.Combine(_dir, "dragón.png");
        using (var file = File.Create(png))
            PngWriter.Write(new RgbaImage(300, 200, Enumerable.Repeat((byte)200, 300 * 200 * 4).ToArray()), file);
        var project = Project();
        var imported = TattooImporter.Import(project, _dir, [png, Path.Combine(_dir, "notas.txt")], TattooZone.LeftArm);
        imported.Added[0].Genders = [PedGender.Female];
        imported.Added[0].UvX = 0.25f;

        var result = ResourceExporter.Export(project, _dir, Output);

        Assert.Single(imported.Warnings);
        Assert.Equal(1, result.Tattoos);
        var ytd = ResourceFile.Read(Path.Combine(Output, "stream", "tattoos", "ropa_tat_000.ytd"));
        var texture = Assert.Single(TextureDictionary.Read(ytd));
        Assert.Equal(("ropa_tat_000", 256, 128, "DXT5"), (texture.Name, texture.Width, texture.Height, texture.Format));

        var item = Assert.Single(XDocument.Load(Path.Combine(Output, "ropa_overlays.xml")).Root!.Element("presetOverlays")!.Elements("Item"));
        Assert.Equal("ropa_tat_000_F", item.Element("nameHash")?.Value);
        Assert.Equal("ZONE_LEFT_ARM", item.Element("zone")?.Value);
        Assert.Equal("GENDER_FEMALE", item.Element("gender")?.Value);
        Assert.Equal("0.250000", item.Element("uvPos")?.Attribute("x")?.Value);
        Assert.Contains("data_file 'PED_OVERLAY_FILE' 'ropa_overlays.xml'", File.ReadAllText(Path.Combine(Output, "fxmanifest.lua")));
        Assert.Contains("ropa_tat_000_F", File.ReadAllText(Path.Combine(Output, "tattoos.json")));
    }

    private static ClothingProject Project(params Drawable[] drawables) =>
        new() { Name = "Ropa 'test'", CollectionName = "ropa", Drawables = [.. drawables] };

    private Drawable Item(string name, PedGender gender, ComponentSlot? component = null, PropSlot? prop = null, int textures = 1)
    {
        File.WriteAllText(Path.Combine(_dir, name + ".ydd"), name + ".ydd");
        var drawable = new Drawable { Name = name, Gender = gender, Component = component, Prop = prop, ModelPath = name + ".ydd" };
        for (var t = 0; t < textures; t++)
        {
            var file = $"{name}_{TextureVariation.LetterFor(t)}.ytd";
            File.WriteAllText(Path.Combine(_dir, file), file);
            drawable.Textures.Add(new TextureVariation { TexturePath = file });
        }
        return drawable;
    }
}
