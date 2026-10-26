using EasyClothes.Core.Export;
using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Validation;

namespace EasyClothes.Core.Tests;

public sealed class ResourceImporterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("easyclothes-resource-").FullName;
    private string ProjectDir => Directory.CreateDirectory(Path.Combine(_root, "project")).FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Garments_keep_their_numbers_options_and_gaps_become_reserved()
    {
        var stream = Directory.CreateDirectory(Path.Combine(_root, "pack", "stream")).FullName;
        var tops = Directory.CreateDirectory(Path.Combine(stream, "[male]", "jbib")).FullName;
        PedVariation.Write("mp_m_ropa",
            new Dictionary<ComponentSlot, IReadOnlyList<VariationDrawable>>
            {
                [ComponentSlot.Top] = [new(2), new(1), new(1) { Skin = true }],
                [ComponentSlot.Shoes] = [new(1) { HeelHeight = 1.3f }],
            },
            new Dictionary<PropSlot, IReadOnlyList<VariationDrawable>> { [PropSlot.Hat] = [new(1) { HairScale = -1f }] })
            .Write(Path.Combine(stream, "mp_m_freemode_01_mp_m_ropa.ymt"));
        File.WriteAllBytes(Path.Combine(stream, "mp_creaturemetadata_m_ropa.ymt"), CreatureMetadata.Write([0], [0]));
        // "c" is left over from an older build: the .ymt only declares two textures.
        Touch(tops, "mp_m_freemode_01_mp_m_ropa^jbib_000_u.ydd", "mp_m_freemode_01_mp_m_ropa^jbib_diff_000_a_uni.ytd", "mp_m_freemode_01_mp_m_ropa^jbib_diff_000_b_uni.ytd", "mp_m_freemode_01_mp_m_ropa^jbib_diff_000_c_uni.ytd");
        Touch(tops, "mp_m_freemode_01_mp_m_ropa^jbib_000_u_1.ydd");
        Touch(tops, "mp_m_freemode_01_mp_m_ropa^jbib_002_r.ydd", "mp_m_freemode_01_mp_m_ropa^jbib_diff_002_a_whi.ytd", "mp_m_freemode_01_mp_m_ropa^jbib_009_u.ydd");
        Touch(stream, "mp_m_freemode_01_mp_m_ropa^feet_000_u.ydd", "mp_m_freemode_01_mp_m_ropa^feet_diff_000_a_uni.ytd");
        Touch(stream, "mp_m_freemode_01_p_mp_m_ropa^p_head_000.ydd", "mp_m_freemode_01_p_mp_m_ropa^p_head_diff_000_a.ytd");
        Touch(stream, "mp_m_freemode_01_mp_m_otra^feet_000_u.ydd");
        var project = new ClothingProject { Name = "Ropa", CollectionName = "ropa" };

        var result = ResourceImporter.Import(project, ProjectDir, Path.Combine(_root, "pack"));

        Assert.True(ResourceImporter.IsResource(Path.Combine(_root, "pack")));
        Assert.Equal("ropa", result.Collection);
        Assert.Equal(["jbib_000", "reservado 1", "jbib_002"], ClothingProject.InGameOrder(project.Drawables.Where(d => d.Component == ComponentSlot.Top)).Select(d => d.Name));
        var top = project.Drawables.Single(d => d.Name == "jbib_000");
        Assert.Equal(["a", "b"], top.Textures.Select(t => Path.GetFileNameWithoutExtension(t.TexturePath)[^5..^4]));
        Assert.Contains(result.Warnings, w => w.StartsWith("1 texturas no las declara el .ymt"));
        Assert.NotNull(top.FirstPersonModelPath);
        Assert.False(top.Flags.AlternateInFirstPerson);
        Assert.Equal(("pack", "mp_m_freemode_01_mp_m_ropa^jbib_000_u.ydd"), (top.Source?.Package, top.Source?.File));
        Assert.True(project.Drawables.Single(d => d.Name == "jbib_002").Skin);
        Assert.Equal(1.3f, project.Drawables.Single(d => d.Component == ComponentSlot.Shoes).Flags.HeelHeight);
        Assert.Equal(HatHair.Remove, project.Drawables.Single(d => d.Prop == PropSlot.Hat).Flags.HatHair);
        Assert.Contains(result.Warnings, w => w.StartsWith("1 números del .ymt"));
        Assert.Contains(result.Warnings, w => w.StartsWith("2 modelos no aparecen"));
    }

    [Fact]
    public void An_exported_resource_imports_back_into_the_same_project()
    {
        var source = new ClothingProject { Name = "Ropa", CollectionName = "ropa" };
        var dir = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        Drawable Garment(string name, PedGender gender, ComponentSlot? component = null, PropSlot? prop = null)
        {
            File.WriteAllText(Path.Combine(dir, name + ".ydd"), name);
            File.WriteAllText(Path.Combine(dir, name + ".ytd"), name);
            var d = new Drawable { Name = name, Gender = gender, Component = component, Prop = prop, ModelPath = name + ".ydd", Textures = [new() { TexturePath = name + ".ytd" }] };
            DrawableOrder.PlaceAtEnd(source, d);
            source.Drawables.Add(d);
            return d;
        }
        Garment("zeta", PedGender.Female, ComponentSlot.Top);
        var hoodie = Garment("alfa", PedGender.Female, ComponentSlot.Top);
        File.WriteAllText(Path.Combine(dir, "alfa_fp.ydd"), "fp");
        File.WriteAllText(Path.Combine(dir, "alfa.yld"), "cloth");
        hoodie.FirstPersonModelPath = "alfa_fp.ydd";
        hoodie.ClothPhysicsPath = "alfa.yld";
        DrawableOrder.Reserve(source, PedGender.Female, ComponentSlot.Top, null);
        Garment("tacon", PedGender.Female, ComponentSlot.Shoes).Flags.HeelHeight = 1.4f;
        Garment("gorro", PedGender.Male, prop: PropSlot.Hat).Flags.HatHair = HatHair.Keep;
        source.AlternateVariations.Add(new AlternateVariation { Gender = PedGender.Female, Component = 11, Index = 656, Sources = [new() { Component = 8, Index = 300 }, new() { Component = 8, Index = 301 }] });
        source.AlternateVariations.Add(new AlternateVariation { Gender = PedGender.Male, Component = 1, Index = 249, Sources = [new() { Component = 11, Index = 664 }] });
        var resource = Path.Combine(_root, "out", "ropa");
        ResourceExporter.Export(source, dir, resource);

        var project = new ClothingProject { Name = "Copia", CollectionName = "x" };
        var result = ResourceImporter.Import(project, ProjectDir, resource);

        Assert.DoesNotContain(result.Warnings, w => !w.Contains("reservados"));
        Assert.Equal("ropa", result.Collection);
        var tops = ClothingProject.InGameOrder(project.Drawables.Where(d => d.Component == ComponentSlot.Top)).ToList();
        Assert.Equal(["zeta", "alfa", "reserved"], tops.Select(d => d.Reserved ? "reserved" : File.ReadAllText(Path.Combine(ProjectDir, d.ModelPath))));
        Assert.Equal("fp", File.ReadAllText(Path.Combine(ProjectDir, tops[1].FirstPersonModelPath!)));
        Assert.True(tops[1].Flags.AlternateInFirstPerson);
        Assert.Equal("cloth", File.ReadAllText(Path.Combine(ProjectDir, tops[1].ClothPhysicsPath!)));
        Assert.Equal(1.4f, project.Drawables.Single(d => d.Component == ComponentSlot.Shoes).Flags.HeelHeight);
        Assert.Equal(HatHair.Keep, project.Drawables.Single(d => d.Prop == PropSlot.Hat).Flags.HatHair);

        Assert.Contains("data_file 'ALTERNATE_VARIATIONS_FILE' 'pedalternatevariations.meta'", File.ReadAllText(Path.Combine(resource, "fxmanifest.lua")));
        Assert.Equal(
            source.AlternateVariations.OrderBy(s => s.Gender).Select(Describe),
            project.AlternateVariations.Select(Describe));
    }

    [Fact]
    public void Alternate_models_and_prop_masks_survive_import_and_build_and_switches_are_checked()
    {
        var stream = Directory.CreateDirectory(Path.Combine(_root, "pack", "stream")).FullName;
        PedVariation.Write("mp_m_ropa",
            new Dictionary<ComponentSlot, IReadOnlyList<VariationDrawable>>
            {
                [ComponentSlot.Decal] = [new(1) { Alternates = 2, PropMask = 5 }, new(1) { Alternates = 2, PropMask = 11, Skin = true }],
            },
            new Dictionary<PropSlot, IReadOnlyList<VariationDrawable>>())
            .Write(Path.Combine(stream, "mp_m_freemode_01_mp_m_ropa.ymt"));
        const string prefix = "mp_m_freemode_01_mp_m_ropa^";
        Touch(stream, prefix + "decl_000_u.ydd", prefix + "decl_000_u_1.ydd", prefix + "decl_000_u_2.ydd", prefix + "decl_diff_000_a_uni.ytd");
        Touch(stream, prefix + "decl_001_r.ydd", prefix + "decl_001_r_1.ydd", prefix + "decl_diff_001_a_whi.ytd");
        File.WriteAllText(Path.Combine(_root, "pack", AlternateVariationsFile.FileName), AlternateVariationsFile.Write(
        [
            new AlternateVariation { Gender = PedGender.Male, Component = 10, Index = 300, Alt = 2, Sources = [new() { Component = 11, Index = 500 }] },
            new AlternateVariation { Gender = PedGender.Male, Component = 10, Index = 300, Alt = 1, Sources = [new() { Component = 11, Index = 501 }] },
            new AlternateVariation { Gender = PedGender.Male, Component = 10, Index = 301, Alt = 2, Sources = [new() { Component = 11, Index = 502 }] },
            new AlternateVariation { Gender = PedGender.Male, Component = 7, Index = 90, Alt = 2, Sources = [new() { Component = 11, Index = 503 }] },
        ]).ToString());
        var project = new ClothingProject { Name = "Ropa", CollectionName = "ropa" };

        var result = ResourceImporter.Import(project, ProjectDir, Path.Combine(_root, "pack"));

        var decals = ClothingProject.InGameOrder(project.Drawables).ToList();
        Assert.Equal([2, 1], decals.Select(d => d.AlternateCount()));
        Assert.Equal([5, 27], decals.Select(d => d.Flags.PropMask));
        Assert.Contains(result.Warnings, w => w.Contains("decl_001_r.ydd") && w.Contains("declara 2 modelos alternativos y solo hay 1"));
        var issues = ProjectValidator.Validate(project, ProjectDir).Select(i => i.Message).ToList();
        Assert.Contains(issues, m => m.StartsWith("decl_001: pedalternatevariations.meta pide su modelo alternativo _2 (número 301"));
        Assert.Contains(issues, m => m.Contains("pide 1 veces el modelo alternativo _2 en teef (hombre)"));
        Assert.DoesNotContain(issues, m => m.StartsWith("decl_000"));

        var resource = Path.Combine(_root, "out", "ropa");
        ResourceExporter.Export(project, ProjectDir, resource);

        Assert.Equal(prefix + "decl_000_u_2.ydd", File.ReadAllText(Path.Combine(resource, "stream", "mp_m_freemode_01", "mp_m_freemode_01_ropa^decl_000_u_2.ydd")));
        var built = PedVariation.Read(ResourceFile.Read(Path.Combine(resource, "stream", "mp_m_freemode_01_ropa.ymt"))).Components[ComponentSlot.Decal];
        Assert.Equal([(2, 5, false), (1, 11, true)], built.Select(d => (d.Alternates, d.PropMask, d.Skin)));
    }

    private static string Describe(AlternateVariation s) =>
        $"{s.Gender} {s.Component}/{s.Index} alt {s.Alt} <- {string.Join(",", s.Sources.Select(a => $"{a.Component}/{a.Index}"))}";

    [Fact]
    public void Reads_the_switches_of_an_eup_style_alternate_variations_file()
    {
        var path = Path.Combine(_root, "pedalternatevariations.meta");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="utf-8"?>
            <CAlternateVariations>
              <peds>
                <Item>
                  <name>mp_m_freemode_01</name>
                  <switches>
                    <Item>
                      <component value="10" />
                      <index value="305" />
                      <alt value="1" />
                      <sourceAssets>
                        <Item><component value="8" /><index value="222" /></Item>
                      </sourceAssets>
                    </Item>
                    <Item><component value="10" /><alt value="1" /></Item>
                  </switches>
                </Item>
                <Item><name>a_m_y_hipster_01</name><switches /></Item>
              </peds>
            </CAlternateVariations>
            """);
        var warnings = new List<string>();

        var switches = AlternateVariationsFile.TryRead(path, warnings);

        Assert.Equal(["Male 10/305 alt 1 <- 8/222"], switches!.Select(Describe));
        Assert.Equal(2, warnings.Count);
        File.WriteAllText(path, "<ShopPedApparel />");
        Assert.Null(AlternateVariationsFile.TryRead(path, warnings));
    }

    private static void Touch(string dir, params string[] names)
    {
        foreach (var name in names) File.WriteAllText(Path.Combine(dir, name), name);
    }
}
