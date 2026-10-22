using EasyClothes.Core.Export;
using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;
using EasyClothes.Core.Rage;

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
        Touch(tops, "mp_m_freemode_01_mp_m_ropa^jbib_000_u.ydd", "mp_m_freemode_01_mp_m_ropa^jbib_diff_000_a_uni.ytd", "mp_m_freemode_01_mp_m_ropa^jbib_diff_000_b_uni.ytd");
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
        var resource = Path.Combine(_root, "out", "ropa");
        ResourceExporter.Export(source, dir, resource);

        var project = new ClothingProject { Name = "Copia", CollectionName = "x" };
        var result = ResourceImporter.Import(project, ProjectDir, resource);

        Assert.DoesNotContain(result.Warnings, w => !w.Contains("reservados"));
        Assert.Equal("ropa", result.Collection);
        var tops = ClothingProject.InGameOrder(project.Drawables.Where(d => d.Component == ComponentSlot.Top)).ToList();
        Assert.Equal(["zeta", "alfa", "reserved"], tops.Select(d => d.Reserved ? "reserved" : File.ReadAllText(Path.Combine(ProjectDir, d.ModelPath))));
        Assert.Equal("fp", File.ReadAllText(Path.Combine(ProjectDir, tops[1].FirstPersonModelPath!)));
        Assert.Equal("cloth", File.ReadAllText(Path.Combine(ProjectDir, tops[1].ClothPhysicsPath!)));
        Assert.Equal(1.4f, project.Drawables.Single(d => d.Component == ComponentSlot.Shoes).Flags.HeelHeight);
        Assert.Equal(HatHair.Keep, project.Drawables.Single(d => d.Prop == PropSlot.Hat).Flags.HatHair);
    }

    private static void Touch(string dir, params string[] names)
    {
        foreach (var name in names) File.WriteAllText(Path.Combine(dir, name), name);
    }
}
